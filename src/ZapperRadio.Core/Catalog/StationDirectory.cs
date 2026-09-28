using System.Text.RegularExpressions;
using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Catalog;

/// <summary>
/// Finds the newest <c>stations-yyyy-MM-dd.txt</c> file linked from the index of the station list on
/// zapperradio.com (built every day by <see cref="StationListBuilder"/>), downloads it into a local cache and parses
/// it. Falls back to the cache when offline. The list used to come from rb2rs as <c>stations-yyyy-MM-dd.rsd</c>, which is the
/// same format; such a file left in the cache still serves offline, until the first new list replaces it.
/// </summary>
public sealed partial class StationDirectory(HttpClient http, string cacheFolder, Uri? indexUri = null)
{
    public static readonly Uri DefaultIndexUri = new("https://zapperradio.com/stations/");

    private readonly Uri _indexUri = indexUri ?? DefaultIndexUri;

    [GeneratedRegex("href=\"(?<file>stations-\\d{4}-\\d{2}-\\d{2}\\.(?:txt|rsd))\"", RegexOptions.IgnoreCase)]
    private static partial Regex StationFileLink();

    [GeneratedRegex("^stations-\\d{4}-\\d{2}-\\d{2}\\.(?:txt|rsd)$", RegexOptions.IgnoreCase)]
    private static partial Regex StationFileName();

    /// <summary>Returns the newest station file name linked from the directory listing, or null.</summary>
    public static string? FindLatestFileName(string indexHtml) =>
        StationFileLink().Matches(indexHtml)
            .Select(m => m.Groups["file"].Value)
            // The yyyy-MM-dd date in the name sorts lexicographically.
            .OrderDescending(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    public async Task<StationCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheFolder);

        string path;
        bool fromCache;
        try
        {
            path = await DownloadLatestAsync(cancellationToken);
            fromCache = false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or StationDirectoryException
                                   && !cancellationToken.IsCancellationRequested)
        {
            path = FindNewestCachedFile() ?? throw new StationDirectoryException(
                $"Could not download the station list from {_indexUri} and there is no local copy. ({ex.Message})", ex);
            fromCache = true;
        }

        var list = await Task.Run(() =>
        {
            using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
            return RsdParser.Parse(reader);
        }, cancellationToken);

        return new StationCatalog(Path.GetFileName(path), list.GeneratedAt, list.Stations, fromCache);
    }

    private async Task<string> DownloadLatestAsync(CancellationToken cancellationToken)
    {
        var indexHtml = await http.GetStringAsync(_indexUri, cancellationToken);
        var fileName = FindLatestFileName(indexHtml)
                       ?? throw new StationDirectoryException($"No stations-*.rsd file found at {_indexUri}.");

        var target = Path.Combine(cacheFolder, fileName);
        if (!File.Exists(target))
        {
            var temp = target + ".download";
            using (var response = await http.GetAsync(new Uri(_indexUri, fileName), HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = File.Create(temp);
                await source.CopyToAsync(destination, cancellationToken);
            }

            File.Move(temp, target, overwrite: true);
        }

        DeleteCachedFilesExcept(target);
        return target;
    }

    private string? FindNewestCachedFile() =>
        Directory.EnumerateFiles(cacheFolder, "stations-*")
            .Where(file => StationFileName().IsMatch(Path.GetFileName(file)))
            // By the date in the name, whichever the extension.
            .OrderDescending(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private void DeleteCachedFilesExcept(string keep)
    {
        foreach (var file in Directory.EnumerateFiles(cacheFolder, "stations-*"))
        {
            if (!string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }
}

public sealed record StationCatalog(string FileName, DateTime? GeneratedAt, IReadOnlyList<Station> Stations, bool FromCache);

public sealed class StationDirectoryException(string message, Exception? inner = null) : Exception(message, inner);
