using System.Globalization;
using System.Text.Json;

namespace ZapperRadio.Core.Catalog;

/// <summary>
/// The rb2rs station list has no popularity data, but it is generated from radio-browser.info.
/// This asks the radio-browser API for a country's most clicked stations, or the world's, and ranks them by stream URL.
/// Results are cached on disk for a day and reused (even when stale) if the API is unreachable.
/// </summary>
public sealed class StationPopularity(HttpClient http, string cacheFolder, IReadOnlyList<Uri>? apiServers = null)
{
    public static readonly IReadOnlyList<Uri> DefaultApiServers =
    [
        new("https://all.api.radio-browser.info/"),
        new("https://de1.api.radio-browser.info/"),
        new("https://de2.api.radio-browser.info/"),
    ];

    /// <summary>How many stations per country are ranked; the rest sort after them.</summary>
    public const int Limit = 1000;

    private static readonly TimeSpan MaxCacheAge = TimeSpan.FromDays(1);

    private static readonly Lazy<Dictionary<string, string>> CountryCodes = new(BuildCountryCodes);

    private readonly IReadOnlyList<Uri> _apiServers = apiServers ?? DefaultApiServers;

    /// <summary>
    /// Returns a map from stream URL to rank (0 = most popular) for the given country name as used in the station
    /// list, or for the whole world when it is null, or an empty map when nothing could be fetched or cached.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> GetRanksAsync(string? country, CancellationToken cancellationToken = default)
    {
        var cacheFile = Path.Combine(cacheFolder, country is null ? "popularity-worldwide.txt" : $"popularity-{SafeFileName(country)}.txt");
        if (File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < MaxCacheAge)
        {
            return ToRanks(await File.ReadAllLinesAsync(cacheFile, cancellationToken));
        }

        foreach (var server in _apiServers)
        {
            try
            {
                var json = await http.GetStringAsync(new Uri(server, BuildQuery(country)), cancellationToken);
                var urls = ParseUrls(json);
                Directory.CreateDirectory(cacheFolder);
                await File.WriteAllLinesAsync(cacheFile, urls, cancellationToken);
                return ToRanks(urls);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Try the next server.
            }
        }

        return File.Exists(cacheFile)
            ? ToRanks(await File.ReadAllLinesAsync(cacheFile, cancellationToken))
            : new Dictionary<string, int>();
    }

    public static string BuildQuery(string? country) =>
        $"json/stations/search?{(country is null ? "" : CountryFilter(country) + "&")}order=clickcount&reverse=true&hidebroken=true&limit={Limit}";

    /// <summary>
    /// The radio-browser query parameter for a country as used in the station list. radio-browser spells
    /// countries differently ("The Netherlands"), so prefer the ISO code. Unknown names fall back to
    /// radio-browser's substring match on the country name.
    /// </summary>
    public static string CountryFilter(string country) =>
        CountryCodes.Value.TryGetValue(country, out var code)
            ? $"countrycode={code}"
            : country is [>= 'A' and <= 'Z', >= 'A' and <= 'Z'] // Some entries already are an ISO code.
            ? $"countrycode={country}"
            : $"country={Uri.EscapeDataString(country)}";

    /// <summary>Finds the country in the station list with the given ISO code (e.g. "NL"), or null if it is not there.</summary>
    public static string? FindCountry(IEnumerable<string> countries, string isoCode) =>
        countries.FirstOrDefault(c =>
            string.Equals(CountryCodes.Value.TryGetValue(c, out var code) ? code : c, isoCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>Extracts station URLs (in the API's order) from a radio-browser station search response.</summary>
    public static List<string> ParseUrls(string json)
    {
        using var document = JsonDocument.Parse(json);
        var urls = new List<string>();
        foreach (var station in document.RootElement.EnumerateArray())
        {
            if (station.TryGetProperty("url", out var url) && url.GetString()?.Trim() is { Length: > 0 } value)
            {
                urls.Add(value);
            }
        }

        return urls;
    }

    private static Dictionary<string, int> ToRanks(IEnumerable<string> urls)
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var url in urls)
        {
            ranks.TryAdd(url, ranks.Count);
        }

        return ranks;
    }

    private static Dictionary<string, string> BuildCountryCodes()
    {
        var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Names in the station list that Windows spells differently (e.g. "Türkiye", "Czechia").
        foreach (var (name, code) in (ReadOnlySpan<(string, string)>)
                 [
                     ("Turkey", "TR"), ("Czech Republic", "CZ"), ("Bosnia and Herzegovina", "BA"), ("Trinidad and Tobago", "TT"),
                     ("Cape Verde", "CV"), ("Saint Lucia", "LC"), ("Saint Vincent and the Grenadines", "VC"),
                     ("Democratic Republic of the Congo", "CD"), ("Antigua and Barbuda", "AG"), ("Saint Kitts and Nevis", "KN"),
                     ("Palestine", "PS"), ("United States Minor Outlying Islands", "UM"), ("Macao", "MO"),
                 ])
        {
            codes[name] = code;
        }

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                if (region.TwoLetterISORegionName.Length == 2 && char.IsLetter(region.TwoLetterISORegionName[0]))
                {
                    codes.TryAdd(region.EnglishName, region.TwoLetterISORegionName);
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return codes;
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
