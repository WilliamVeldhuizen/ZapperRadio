using ZapperRadio.Core.Catalog;
using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Tests;

public class CatalogTests
{
    [Fact]
    public void Parse_ReadsTimestampAndStations()
    {
        const string rsd = "2026-09-16 12:13:15\n" +
                           "Qmusic\t-\t \tNetherlands\t \thttps://icecast-qmusicnl-cdp.triple-it.nl/Qmusic_nl_live_96.mp3\n" +
                           "Sanctuary - MP3\t-\tchristian,worship\tNew Zealand\tEnglish\thttps://rhema-radio.streamguys1.com/rhema-sanctuary.mp3\r\n" +
                           "Broken line without tabs\n" +
                           "No url\t-\t\t\t\tnot-a-url\n";

        var result = RsdParser.Parse(new StringReader(rsd));

        Assert.Equal(new DateTime(2026, 9, 16, 12, 13, 15), result.GeneratedAt);
        Assert.Equal(2, result.Stations.Count);
        Assert.Equal(new Station("Qmusic", "", "Netherlands", "", "https://icecast-qmusicnl-cdp.triple-it.nl/Qmusic_nl_live_96.mp3"), result.Stations[0]);
        Assert.Equal("New Zealand · christian,worship", result.Stations[1].Subtitle);
        Assert.Equal("English", result.Stations[1].Language);
    }

    [Fact]
    public void FindLatestFileName_PicksNewestDate()
    {
        const string html = """
            <a href="latest.zip">latest.zip</a>
            <a href="stations-2026-09-03.rsd">stations-2026-09-03.rsd</a>
            <a href="stations-2026-09-16.rsd">stations-2026-09-16.rsd</a>
            <a href="stations-2026-09-15.rsd">stations-2026-09-15.rsd</a>
            """;

        Assert.Equal("stations-2026-09-16.rsd", StationDirectory.FindLatestFileName(html));
        Assert.Null(StationDirectory.FindLatestFileName("<html></html>"));
    }

    [Fact]
    public void FindLatestFileName_ReadsTheIndexTheBuilderWrites()
    {
        var html = StationListBuilder.IndexHtml("stations-2026-09-28.txt", new DateTime(2026, 9, 28, 3, 23, 0), 50_302);

        Assert.Equal("stations-2026-09-28.txt", StationDirectory.FindLatestFileName(html));
    }

    [Fact]
    public async Task LoadAsync_DownloadsLatestAndFallsBackToCacheWhenOffline()
    {
        var cache = Path.Combine(Path.GetTempPath(), "ZapperRadioTests", Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new FakeHandler(uri => uri.AbsolutePath switch
            {
                "/stations/" => "<a href=\"stations-2026-09-15.txt\"></a><a href=\"stations-2026-09-16.txt\"></a>",
                "/stations/stations-2026-09-16.txt" => "2026-09-16 12:13:15\nA\t-\t\tNL\t\thttp://a.example/stream\n",
                _ => null,
            });
            // A list from rb2rs, from before the list moved to zapperradio.com.
            File.WriteAllText(Path.Combine(cache.EnsureDirectory(), "stations-2026-09-01.rsd"), "old");

            var online = await new StationDirectory(new HttpClient(handler), cache).LoadAsync();

            Assert.False(online.FromCache);
            Assert.Equal("stations-2026-09-16.txt", online.FileName);
            Assert.Single(online.Stations);
            Assert.Equal(["stations-2026-09-16.txt"], Directory.GetFiles(cache).Select(Path.GetFileName));

            handler.Offline = true;
            var offline = await new StationDirectory(new HttpClient(handler), cache).LoadAsync();

            Assert.True(offline.FromCache);
            Assert.Equal("A", offline.Stations[0].Name);
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_UsesAnOldRb2rsListWhileOffline()
    {
        var cache = Path.Combine(Path.GetTempPath(), "ZapperRadioTests", Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new FakeHandler(_ => null) { Offline = true };
            File.WriteAllText(Path.Combine(cache.EnsureDirectory(), "stations-2026-09-01.rsd"), "2026-09-01 12:00:00\nA\t-\t\tNL\t\thttp://a.example/stream\n");

            var offline = await new StationDirectory(new HttpClient(handler), cache).LoadAsync();

            Assert.True(offline.FromCache);
            Assert.Equal("stations-2026-09-01.rsd", offline.FileName);
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_UsesTheBundledListWhenItIsTheNewestAndCopiesItInsteadOfDownloading()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZapperRadioTests", Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "cache");
        var bundled = Path.Combine(root, "bundled");
        try
        {
            const string list = "2026-09-20 03:23:00\nBundled\t-\t\tNL\t\thttp://b.example/stream\n";
            File.WriteAllText(Path.Combine(bundled.EnsureDirectory(), "stations-2026-09-20.txt"), list);
            File.WriteAllText(Path.Combine(cache.EnsureDirectory(), "stations-2026-09-01.rsd"), "2026-09-01 12:00:00\nOld\t-\t\tNL\t\thttp://a.example/stream\n");

            // Offline, the bundled list is newer than the one in the cache.
            var offline = await new StationDirectory(new HttpClient(new FakeHandler(_ => null) { Offline = true }), cache, bundledFolder: bundled).LoadAsync();
            Assert.True(offline.FromCache);
            Assert.Equal("Bundled", offline.Stations[0].Name);

            // Online, while the bundled list is still the newest one, it is copied rather than downloaded.
            var downloads = 0;
            var handler = new FakeHandler(uri =>
            {
                if (uri.AbsolutePath == "/stations/")
                {
                    return "<a href=\"stations-2026-09-20.txt\"></a>";
                }

                downloads++;
                return null;
            });
            var online = await new StationDirectory(new HttpClient(handler), cache, bundledFolder: bundled).LoadAsync();
            Assert.False(online.FromCache);
            Assert.Equal(0, downloads);
            Assert.Equal(["stations-2026-09-20.txt"], Directory.GetFiles(cache).Select(Path.GetFileName));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static RadioBrowserStation Entry(string name, string url, int lastCheckOk = 1, string? lastWorked = null,
        string tags = "", int clicks = 0) =>
        new(name, url, tags, "Netherlands", "dutch", lastCheckOk, lastWorked, clicks, 0);

    [Fact]
    public void StationListBuilder_LeavesOutStationsDownForMoreThanAMonth()
    {
        var now = new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero);

        Assert.True(StationListBuilder.IsAlive(Entry("A", "http://a.example/"), now));
        Assert.True(StationListBuilder.IsAlive(Entry("A", "http://a.example/", 0, "2026-09-20T10:00:00Z"), now));
        Assert.False(StationListBuilder.IsAlive(Entry("A", "http://a.example/", 0, "2026-08-01T10:00:00Z"), now));
        Assert.False(StationListBuilder.IsAlive(Entry("A", "http://a.example/", 0, null), now));
    }

    [Fact]
    public void StationListBuilder_CleansNamesAndTagsAndSkipsWhatCannotPlay()
    {
        var station = StationListBuilder.ToStation(Entry("\t Radio\n  One ", " http://one.example/stream ", tags: "pop,rock, pop ,,hits"));

        Assert.Equal(new Station("Radio One", "pop, rock, hits", "Netherlands", "dutch", "http://one.example/stream"), station);
        Assert.Null(StationListBuilder.ToStation(Entry("   ", "http://one.example/stream")));
        Assert.Null(StationListBuilder.ToStation(Entry("Radio", "ftp://one.example/stream")));
        Assert.Null(StationListBuilder.ToStation(Entry("Radio", "not a url")));
    }

    [Fact]
    public void StationListBuilder_KeepsOneStationPerStreamNamedByTheMostClicked()
    {
        var now = new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero);
        var stations = StationListBuilder.Build(
        [
            Entry("Zeta", "http://z.example/"),
            Entry("Copy of Alpha", "http://a.example/", clicks: 3),
            Entry("Alpha", "http://a.example/", clicks: 40),
        ], now);

        Assert.Equal(["Alpha", "Zeta"], stations.Select(s => s.Name));
    }

    [Fact]
    public void StationListBuilder_WritesWhatTheParserReads()
    {
        var generatedAt = new DateTime(2026, 9, 28, 3, 23, 5);
        Station[] stations =
        [
            new("Alpha", "pop, hits", "Netherlands", "dutch", "http://a.example/"),
            new("Beta", "", "", "", "https://b.example/live"),
        ];

        var writer = new StringWriter();
        StationListBuilder.Write(writer, generatedAt, stations);
        var list = RsdParser.Parse(new StringReader(writer.ToString()));

        Assert.Equal(generatedAt, list.GeneratedAt);
        Assert.Equal(stations, list.Stations);
        Assert.Equal("stations-2026-09-28.txt", StationListBuilder.FileName(generatedAt));
    }

    [Fact]
    public async Task StationListBuilder_ReadsAPageOfTheApi()
    {
        const string json = """
            [{"changeuuid":"x","name":"Alpha","url":"http://a.example/","tags":"pop","country":"Netherlands",
              "language":"dutch","lastcheckok":1,"lastcheckoktime_iso8601":"2026-09-27T10:00:00Z","clickcount":5,"votes":2,
              "favicon":"http://a.example/logo.png"}]
            """;

        var entries = await StationListBuilder.ReadAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));

        Assert.Equal(Entry("Alpha", "http://a.example/", 1, "2026-09-27T10:00:00Z", "pop", 5) with { Votes = 2 }, Assert.Single(entries));
    }

    [Theory]
    [InlineData("qmusic", null, true)]
    [InlineData("QMUSIC nether", null, true)]
    [InlineData("qmusic", "Netherlands", true)]
    [InlineData("qmusic", "Belgium", false)]
    [InlineData("pop", null, true)]
    [InlineData("jazz", null, false)]
    [InlineData("", null, true)]
    public void StationFilter_MatchesAllTerms(string query, string? country, bool expected)
    {
        var station = new Station("Q music Nederland", "pop", "Netherlands", "", "https://stream.qmusic.nl/qmusic/aachigh");

        Assert.Equal(expected, new StationFilter(query, country).Matches(station));
    }

    [Theory]
    // The whole name, then the name starting with the term, a word in it, the middle of it, the tags, the country, a typo.
    [InlineData("radio 538", "Radio 538", "Radio 538 Non-stop", "Hitradio 538", "Top 40")]
    [InlineData("538", "538 Hitzone", "Radio 538", "Hitradio538", "Top 40")]
    [InlineData("qmusic", "Q-Music", "Qmusic Foute Uur", "Joe", "Qmusik")]
    [InlineData("netherlands", "Netherlands Radio", "Radio 538", "Radio Nethrlands")]
    public void StationFilter_RanksBetterMatchesFirst(string query, params string[] namesInOrder)
    {
        var stations = new Dictionary<string, Station>
        {
            ["Radio 538"] = new("Radio 538", "pop", "Netherlands", "", "http://a.example/"),
            ["538 Hitzone"] = new("538 Hitzone", "hits", "Netherlands", "", "http://b.example/"),
            ["Radio 538 Non-stop"] = new("Radio 538 Non-stop", "pop", "Netherlands", "", "http://c.example/"),
            ["Hitradio 538"] = new("Hitradio 538", "", "Netherlands", "", "http://d.example/"),
            ["Hitradio538"] = new("Hitradio538", "", "Netherlands", "", "http://d2.example/"),
            ["Top 40"] = new("Top 40", "538,radio", "Netherlands", "", "http://e.example/"),
            ["Radio Nethrlands"] = new("Radio Nethrlands", "", "Germany", "", "http://f.example/"),
            ["Q-Music"] = new("Q-Music", "", "Netherlands", "", "http://g.example/"),
            ["Qmusic Foute Uur"] = new("Qmusic Foute Uur", "", "Netherlands", "", "http://h.example/"),
            ["Joe"] = new("Joe", "qmusic", "Belgium", "", "http://i.example/"),
            ["Qmusik"] = new("Qmusik", "", "Germany", "", "http://j.example/"),
            ["Netherlands Radio"] = new("Netherlands Radio", "", "Netherlands", "", "http://k.example/"),
        };
        var filter = new StationFilter(query);

        var scores = namesInOrder.Select(name => filter.Relevance(stations[name])).ToList();

        Assert.DoesNotContain(null, scores);
        Assert.Equal(scores.Order(), scores);
        Assert.Equal(scores.Count, scores.Distinct().Count());
    }

    [Fact]
    public void StationFilter_EmptyQueryMatchesEveryStationEqually()
    {
        var filter = new StationFilter("");

        Assert.Equal(filter.Relevance(new Station("A", "", "", "", "http://a.example/")),
                     filter.Relevance(new Station("Radio B", "pop", "Belgium", "", "http://b.example/")));
    }
}

internal static class TestPathExtensions
{
    public static string EnsureDirectory(this string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
