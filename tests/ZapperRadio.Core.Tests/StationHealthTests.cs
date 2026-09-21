using System.Text;
using ZapperRadio.Core.Catalog;

namespace ZapperRadio.Core.Tests;

public class StationHealthTests
{
    private const string BrokenJson = """
        [
          {"name":"Gone","url":"http://gone.example/ ","lastcheckoktime_iso8601":"2026-01-05T10:00:00Z"},
          {"name":"Never worked","url":"http://never.example/","lastcheckoktime_iso8601":null},
          {"name":"No url"},
          {"name":"Gone, listed again","url":"http://gone.example/","lastcheckoktime_iso8601":"2026-03-01T08:30:00Z"}
        ]
        """;

    [Fact]
    public async Task ParseBrokenAsync_KeepsTheLastMomentEachUrlWorked()
    {
        var broken = await StationHealth.ParseBrokenAsync(new MemoryStream(Encoding.UTF8.GetBytes(BrokenJson)));

        Assert.Equal(2, broken.Count);
        Assert.Equal(new DateTime(2026, 3, 1, 8, 30, 0, DateTimeKind.Utc), broken["http://gone.example/"]);
        Assert.Equal(DateTimeKind.Utc, broken["http://gone.example/"]!.Value.Kind);
        Assert.Null(broken["http://never.example/"]);
    }

    [Fact]
    public async Task GetBrokenAsync_CachesForADayAndFallsBackToTheCache()
    {
        var cache = Path.Combine(Path.GetTempPath(), "ZapperRadioTests", Guid.NewGuid().ToString("N"));
        try
        {
            var requests = 0;
            var handler = new FakeHandler(uri =>
            {
                requests++;
                return uri.AbsolutePath == "/json/stations/broken" ? BrokenJson : null;
            });
            var health = new StationHealth(new HttpClient(handler), cache, [new Uri("https://api.example/")]);

            var online = await health.GetBrokenAsync();
            Assert.Equal(2, online.Count);

            // A fresh cache is used without asking the API again, and reads back the same moments.
            handler.Offline = true;
            var cached = await health.GetBrokenAsync();
            Assert.Equal(online, cached);
            Assert.Equal(1, requests);

            // A stale cache is still better than nothing when offline.
            File.SetLastWriteTimeUtc(Path.Combine(cache, StationHealth.CacheFileName), DateTime.UtcNow.AddDays(-3));
            Assert.Equal(online, await health.GetBrokenAsync());
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task GetBrokenAsync_WithoutApiOrCache_KnowsOfNothingBroken()
    {
        var cache = Path.Combine(Path.GetTempPath(), "ZapperRadioTests", Guid.NewGuid().ToString("N"));
        var health = new StationHealth(new HttpClient(new FakeHandler(_ => null) { Offline = true }), cache, [new Uri("https://api.example/")]);

        Assert.Empty(await health.GetBrokenAsync());
    }
}
