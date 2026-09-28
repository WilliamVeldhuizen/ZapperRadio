using System.Globalization;
using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Catalog;

/// <summary>
/// Parses the station list format, which is the <c>.rsd</c> format of rb2rs where the list used to come from: a
/// timestamp on the first line, followed by one tab-separated station per line: name, (unused), tags, country,
/// language, url. <see cref="StationListBuilder.Write"/> writes it.
/// </summary>
public static class RsdParser
{
    private const int NameColumn = 0;
    private const int TagsColumn = 2;
    private const int CountryColumn = 3;
    private const int LanguageColumn = 4;
    private const int UrlColumn = 5;

    public static StationList Parse(TextReader reader)
    {
        DateTime? generatedAt = null;
        var stations = new List<Station>();

        var first = true;
        while (reader.ReadLine() is { } line)
        {
            if (first)
            {
                first = false;
                if (DateTime.TryParseExact(line.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var timestamp))
                {
                    generatedAt = timestamp;
                    continue;
                }
            }

            if (TryParseStation(line) is { } station)
            {
                stations.Add(station);
            }
        }

        return new StationList(generatedAt, stations);
    }

    public static Station? TryParseStation(string line)
    {
        var fields = line.Split('\t');
        if (fields.Length <= UrlColumn)
        {
            return null;
        }

        var name = fields[NameColumn].Trim();
        var url = fields[UrlColumn].Trim();
        if (name.Length == 0
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return new Station(name, fields[TagsColumn].Trim(), fields[CountryColumn].Trim(), fields[LanguageColumn].Trim(), url);
    }
}

public sealed record StationList(DateTime? GeneratedAt, IReadOnlyList<Station> Stations);
