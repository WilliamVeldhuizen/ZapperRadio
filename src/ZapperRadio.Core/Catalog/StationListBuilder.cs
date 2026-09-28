using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Catalog;

/// <summary>
/// Builds the station list the app downloads, from radio-browser.info's full list of stations: the list published
/// on zapperradio.com/stations every day by tools/StationListBuilder. This is where the list is cleaned up before
/// any app sees it, so what is done here is done once for everyone instead of on every PC.
/// </summary>
public static class StationListBuilder
{
    /// <summary>
    /// How long a station may fail radio-browser's checks before it is left out. A shorter outage stays in the
    /// list, and the app marks it as down (see <see cref="StationHealth"/>) instead of hiding it.
    /// </summary>
    public static readonly TimeSpan MaxDowntime = TimeSpan.FromDays(30);

    /// <summary>
    /// How many stations are asked for at a time. Without a limit the API answers with only the first thousand, and
    /// one request for all of them is refused, so the list is read in pages.
    /// </summary>
    public const int PageSize = 10_000;

    /// <summary>
    /// The request for one page of every station, working or not, in the order of their ids, which does not
    /// change while the pages are read.
    /// </summary>
    public static string PageQuery(int offset) =>
        $"json/stations?hidebroken=false&order=stationuuid&limit={PageSize}&offset={offset}";

    /// <summary>Reads one page of radio-browser's JSON array of stations.</summary>
    public static async Task<List<RadioBrowserStation>> ReadAsync(Stream json, CancellationToken cancellationToken = default)
    {
        var entries = new List<RadioBrowserStation>();
        await foreach (var entry in JsonSerializer.DeserializeAsyncEnumerable(json, RadioBrowserJsonContext.Default.RadioBrowserStation, cancellationToken))
        {
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>The stations worth listing, sorted by name.</summary>
    public static IReadOnlyList<Station> Build(IEnumerable<RadioBrowserStation> entries, DateTimeOffset now) =>
        entries
            .Where(e => IsAlive(e, now))
            .Select(e => (Entry: e, Station: ToStation(e)))
            .Where(s => s.Station is not null)
            // The same stream listed twice is one station, and the one most people picked names it best.
            .GroupBy(s => s.Station!.Url, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(s => s.Entry.ClickCount).ThenByDescending(s => s.Entry.Votes).First().Station!)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Url, StringComparer.Ordinal)
            .ToList();

    /// <summary>Whether a station worked at its last check, or at least within <see cref="MaxDowntime"/>.</summary>
    public static bool IsAlive(RadioBrowserStation entry, DateTimeOffset now)
    {
        if (entry.LastCheckOk == 1)
        {
            return true;
        }

        return DateTimeOffset.TryParse(entry.LastCheckOkTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var lastWorked)
               && now - lastWorked <= MaxDowntime;
    }

    /// <summary>The station as the app lists it, or null when it has no name or no stream the app can play.</summary>
    public static Station? ToStation(RadioBrowserStation entry)
    {
        var name = Clean(entry.Name);
        var url = Clean(entry.Url);
        if (name.Length == 0
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var tags = string.Join(", ", Clean(entry.Tags).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        return new Station(name, tags, Clean(entry.Country), Clean(entry.Language), url);
    }

    /// <summary>
    /// Writes the list in the tab-separated format <see cref="RsdParser"/> reads: the time it was made on the first
    /// line, then one station per line with its name, an unused column, tags, country, language and stream URL.
    /// </summary>
    public static void Write(TextWriter writer, DateTime generatedAt, IEnumerable<Station> stations)
    {
        writer.NewLine = "\n";
        writer.WriteLine(generatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        foreach (var station in stations)
        {
            writer.WriteLine(string.Join('\t', station.Name, "-", station.Tags, station.Country, station.Language, station.Url));
        }
    }

    /// <summary>The file name of the list made on a day, which the app finds through the index next to it.</summary>
    public static string FileName(DateTime generatedAt) => $"stations-{generatedAt:yyyy-MM-dd}.txt";

    /// <summary>
    /// The index the app reads to find the newest list (<see cref="StationDirectory.FindLatestFileName"/>), which is
    /// also a page a person can open.
    /// </summary>
    public static string IndexHtml(string fileName, DateTime generatedAt, int count) =>
        $"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="robots" content="noindex">
        <title>ZapperRadio station list</title>
        </head>
        <body>
        <h1>ZapperRadio station list</h1>
        <p>{count.ToString("N0", CultureInfo.InvariantCulture)} stations, made {generatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC
        from <a href="https://www.radio-browser.info/">radio-browser.info</a>. The app downloads it once a day.</p>
        <p><a href="{fileName}">{fileName}</a></p>
        </body>
        </html>

        """;

    /// <summary>Collapses whitespace, which also removes tabs and line breaks that would break the format.</summary>
    private static string Clean(string? text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>A station as radio-browser.info's API describes it; only the fields the list is built from.</summary>
public sealed record RadioBrowserStation(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("tags")] string? Tags,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("lastcheckok")] int LastCheckOk,
    [property: JsonPropertyName("lastcheckoktime_iso8601")] string? LastCheckOkTime,
    [property: JsonPropertyName("clickcount")] int ClickCount,
    [property: JsonPropertyName("votes")] int Votes);

[JsonSerializable(typeof(RadioBrowserStation))]
internal sealed partial class RadioBrowserJsonContext : JsonSerializerContext;
