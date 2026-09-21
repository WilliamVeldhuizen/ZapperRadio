using System.Globalization;
using System.Text.Json;

namespace ZapperRadio.Core.Catalog;

/// <summary>
/// Which stations are down. The rb2rs station list has no health data, but radio-browser.info, which it is generated
/// from, checks every station regularly and lists the ones that failed their last check, with the moment each one
/// last worked. That list is fetched in one request and matched on stream URL, like the popularity.
/// It is cached on disk for a day and reused (even when stale) if the API is unreachable.
/// </summary>
public sealed class StationHealth(HttpClient http, string cacheFolder, IReadOnlyList<Uri>? apiServers = null)
{
    /// <summary>The file the broken stations are kept in; the name the cache settings count it by.</summary>
    public const string CacheFileName = "health-broken.txt";

    /// <summary>
    /// The broken stations of all countries. There is no way to ask for fewer fields, so it is a few megabytes of
    /// JSON, once a day; what is kept of it is the URL and a date.
    /// </summary>
    public const string Query = "json/stations/broken?limit=100000";

    private static readonly TimeSpan MaxCacheAge = TimeSpan.FromDays(1);

    private readonly IReadOnlyList<Uri> _apiServers = apiServers ?? StationPopularity.DefaultApiServers;

    /// <summary>
    /// Returns the stations that failed their last check, by stream URL, with the moment each last worked (null when
    /// it never did), or an empty map when nothing could be fetched or cached.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, DateTime?>> GetBrokenAsync(CancellationToken cancellationToken = default)
    {
        var cacheFile = Path.Combine(cacheFolder, CacheFileName);
        if (File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < MaxCacheAge)
        {
            return ReadCache(await File.ReadAllLinesAsync(cacheFile, cancellationToken));
        }

        foreach (var server in _apiServers)
        {
            try
            {
                await using var json = await http.GetStreamAsync(new Uri(server, Query), cancellationToken);
                var broken = await ParseBrokenAsync(json, cancellationToken);
                Directory.CreateDirectory(cacheFolder);
                await File.WriteAllLinesAsync(cacheFile, WriteCache(broken), cancellationToken);
                return broken;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Try the next server.
            }
        }

        return File.Exists(cacheFile)
            ? ReadCache(await File.ReadAllLinesAsync(cacheFile, cancellationToken))
            : new Dictionary<string, DateTime?>();
    }

    /// <summary>Reads the stream URLs and the moment each last worked from a radio-browser station list.</summary>
    public static async Task<Dictionary<string, DateTime?>> ParseBrokenAsync(Stream json, CancellationToken cancellationToken = default)
    {
        using var document = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
        var broken = new Dictionary<string, DateTime?>(StringComparer.Ordinal);
        foreach (var station in document.RootElement.EnumerateArray())
        {
            if (!station.TryGetProperty("url", out var url) || url.GetString()?.Trim() is not { Length: > 0 } value)
            {
                continue;
            }

            DateTime? lastWorked = station.TryGetProperty("lastcheckoktime_iso8601", out var time)
                                   && time.ValueKind == JsonValueKind.String
                                   && DateTime.TryParse(time.GetString(), CultureInfo.InvariantCulture,
                                       DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;

            // A URL listed twice is broken either way; the later of the two moments it worked is the one that counts.
            broken[value] = broken.TryGetValue(value, out var earlier) && earlier > lastWorked ? earlier : lastWorked;
        }

        return broken;
    }

    private static IEnumerable<string> WriteCache(IReadOnlyDictionary<string, DateTime?> broken) =>
        broken.Select(pair => $"{pair.Key}\t{pair.Value?.ToString("O", CultureInfo.InvariantCulture)}");

    private static Dictionary<string, DateTime?> ReadCache(IEnumerable<string> lines)
    {
        var broken = new Dictionary<string, DateTime?>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var tab = line.LastIndexOf('\t');
            if (tab <= 0)
            {
                continue;
            }

            broken[line[..tab]] = DateTime.TryParse(line[(tab + 1)..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
                ? time
                : null;
        }

        return broken;
    }
}
