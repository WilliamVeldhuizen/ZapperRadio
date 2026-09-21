using ZapperRadio.Core.Catalog;

namespace ZapperRadio.Core.Tests;

public class StationPopularityTests
{
    [Theory]
    [InlineData("Netherlands", "countrycode=NL")]
    [InlineData("Germany", "countrycode=DE")]
    [InlineData("United Kingdom", "countrycode=GB")]
    [InlineData("Turkey", "countrycode=TR")]
    [InlineData("VG", "countrycode=VG")]
    [InlineData("Atlantis Island", "country=Atlantis%20Island")]
    public void BuildQuery_PrefersCountryCode(string country, string expectedFilter)
    {
        var query = StationPopularity.BuildQuery(country);

        Assert.Contains(expectedFilter, query);
        Assert.Contains("order=clickcount&reverse=true", query);
    }

    [Fact]
    public void BuildQuery_WithoutCountry_RanksTheWholeWorld()
    {
        var query = StationPopularity.BuildQuery(null);

        Assert.StartsWith("json/stations/search?order=clickcount&reverse=true", query);
        Assert.DoesNotContain("country", query);
    }

    [Theory]
    [InlineData("NL", "Netherlands")]
    [InlineData("gb", "United Kingdom")]
    [InlineData("TR", "Turkey")]
    [InlineData("VG", "VG")]
    [InlineData("FR", null)]
    public void FindCountry_MatchesIsoCode(string isoCode, string? expected)
    {
        string[] countries = ["All countries", "Germany", "Netherlands", "Turkey", "United Kingdom", "VG"];

        Assert.Equal(expected, StationPopularity.FindCountry(countries, isoCode));
    }

    [Fact]
    public void ParseUrls_KeepsApiOrder()
    {
        const string json = """[{"name":"B","url":"http://b.example/ "},{"name":"No url"},{"name":"A","url":"http://a.example/"}]""";

        Assert.Equal(["http://b.example/", "http://a.example/"], StationPopularity.ParseUrls(json));
    }

    [Fact]
    public async Task GetRanksAsync_RanksByApiOrderAndFallsBackToCache()
    {
        var cache = Path.Combine(Path.GetTempPath(), "ZapperRadioTests", Guid.NewGuid().ToString("N"));
        try
        {
            var requests = 0;
            var handler = new FakeHandler(uri =>
            {
                requests++;
                return uri.Query.Contains("countrycode=NL")
                    ? """[{"url":"http://popular.example/"},{"url":"http://second.example/"},{"url":"http://popular.example/"}]"""
                    : null;
            });
            var popularity = new StationPopularity(new HttpClient(handler), cache, [new Uri("https://api.example/")]);

            var online = await popularity.GetRanksAsync("Netherlands");
            Assert.Equal(0, online["http://popular.example/"]);
            Assert.Equal(1, online["http://second.example/"]);
            Assert.Equal(2, online.Count);

            // A fresh cache is used without asking the API again.
            handler.Offline = true;
            var cached = await popularity.GetRanksAsync("Netherlands");
            Assert.Equal(online, cached);
            Assert.Equal(1, requests);

            // A stale cache is still better than nothing when offline.
            File.SetLastWriteTimeUtc(Directory.GetFiles(cache).Single(), DateTime.UtcNow.AddDays(-3));
            Assert.Equal(online, await popularity.GetRanksAsync("Netherlands"));

            Assert.Empty(await popularity.GetRanksAsync("Belgium"));
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }
}
