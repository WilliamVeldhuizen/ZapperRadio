using System.Text.Json;
using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Catalog;

/// <summary>
/// The station list has no logos, but it is built from radio-browser.info, whose stations
/// carry a "favicon" URL. This looks a station up by name, prefers the entry whose stream URL matches,
/// and confirms the favicon actually loads before using it. Results are cached on disk per station URL
/// and reused indefinitely, since a station's logo (or the lack of one) rarely changes.
/// </summary>
public sealed class StationLogos(HttpClient http, string cacheFolder, IReadOnlyList<Uri>? apiServers = null)
{
    public static readonly IReadOnlyList<Uri> DefaultApiServers = StationPopularity.DefaultApiServers;

    private readonly IReadOnlyList<Uri> _apiServers = apiServers ?? DefaultApiServers;
    private readonly Dictionary<string, Task<string?>> _lookups = new(StringComparer.Ordinal);

    /// <summary>Returns the station's logo URL, or null when none could be found. Never throws.</summary>
    public Task<string?> GetLogoUrlAsync(Station station, CancellationToken cancellationToken = default)
    {
        lock (_lookups)
        {
            if (!_lookups.TryGetValue(station.Url, out var task))
            {
                task = LoadAsync(station, cancellationToken);
                _lookups[station.Url] = task;
            }

            return task;
        }
    }

    /// <summary>
    /// Throws away every logo found so far, on disk and in this session, so they are looked up again. For when a
    /// station changed its logo, or the wrong one was found for it.
    /// </summary>
    public void Clear()
    {
        lock (_lookups)
        {
            _lookups.Clear();
        }

        if (!Directory.Exists(cacheFolder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(cacheFolder, "logo-*.txt"))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A logo that cannot be deleted is simply used again.
            }
        }
    }

    private async Task<string?> LoadAsync(Station station, CancellationToken cancellationToken)
    {
        var cacheFile = Path.Combine(cacheFolder, $"logo-{SafeFileName(station.Url)}.txt");
        if (File.Exists(cacheFile))
        {
            var cached = await File.ReadAllTextAsync(cacheFile, cancellationToken);
            return cached.Length > 0 ? cached : null;
        }

        foreach (var server in _apiServers)
        {
            try
            {
                var json = await http.GetStringAsync(new Uri(server, BuildQuery(station)), cancellationToken);
                var logo = await FirstReachableAsync(FindCandidates(json, station.Url), cancellationToken);
                Directory.CreateDirectory(cacheFolder);
                await File.WriteAllTextAsync(cacheFile, logo ?? "", cancellationToken);
                return logo;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Try the next server.
            }
        }

        return null;
    }

    /// <summary>
    /// A common name like "Radio 10" exists in several countries, so the country narrows the search
    /// down to the right one whenever the station list gives one.
    /// </summary>
    public static string BuildQuery(Station station)
    {
        var filter = $"name={Uri.EscapeDataString(station.Name)}";
        if (station.Country.Length > 0)
        {
            filter += $"&{StationPopularity.CountryFilter(station.Country)}";
        }

        return $"json/stations/search?{filter}&limit=20&hidebroken=true";
    }

    /// <summary>Favicon URLs from a radio-browser search response, with the entry matching <paramref name="url"/> first.</summary>
    public static List<string> FindCandidates(string json, string url)
    {
        using var document = JsonDocument.Parse(json);
        var matching = new List<string>();
        var others = new List<string>();
        foreach (var station in document.RootElement.EnumerateArray())
        {
            if (!station.TryGetProperty("favicon", out var faviconProperty)
                || faviconProperty.GetString()?.Trim() is not { Length: > 0 } favicon
                || !Uri.TryCreate(favicon, UriKind.Absolute, out _))
            {
                continue;
            }

            var stationUrl = station.TryGetProperty("url", out var u) ? u.GetString()?.Trim() : null;
            var bucket = string.Equals(stationUrl?.TrimEnd('/'), url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ? matching : others;
            bucket.Add(favicon);
        }

        matching.AddRange(others);
        return matching.Distinct().ToList();
    }

    private async Task<string?> FirstReachableAsync(IEnumerable<string> candidates, CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates)
        {
            if (await IsReachableAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<bool> IsReachableAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return false;
        }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
        return safe.Length > 120 ? safe[..120] : safe;
    }
}
