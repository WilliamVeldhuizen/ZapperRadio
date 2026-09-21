using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Catalog;

/// <summary>How well a station matches a search.</summary>
public enum StationMatch
{
    None,
    /// <summary>Every term matches, but at least one only with a typo (see <see cref="Levenshtein"/>).</summary>
    Fuzzy,
    Exact,
}

/// <summary>Search over name, tags and country: every word in the query must match somewhere.</summary>
public sealed class StationFilter
{
    private readonly string[] _terms;
    private readonly string? _country;
    private readonly string _compactQuery;

    public StationFilter(string? query, string? country = null)
    {
        _terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _country = string.IsNullOrWhiteSpace(country) ? null : country;
        _compactQuery = Compact(query ?? string.Empty);
    }

    public bool IsEmpty => _terms.Length == 0 && _country is null;

    public bool Matches(Station station) => Match(station) != StationMatch.None;

    public StationMatch Match(Station station) =>
        Evaluate(station, out var typo) is null ? StationMatch.None
        : typo ? StationMatch.Fuzzy
        : StationMatch.Exact;

    /// <summary>
    /// How well the station matches, lower being better, or null when it does not match. 0 is the query being the
    /// station's whole name; after that every term adds where it was found (see <see cref="Place"/>), so "538"
    /// puts Radio 538 above a station that only has 538 in its tags. Equal scores are left to the popularity.
    /// </summary>
    public int? Relevance(Station station) => Evaluate(station, out _);

    private int? Evaluate(Station station, out bool typo)
    {
        typo = false;
        if (_country is not null && !string.Equals(station.Country, _country, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var compactName = Compact(station.Name);
        if (_compactQuery.Length > 0 && string.Equals(compactName, _compactQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var score = 1;
        foreach (var term in _terms)
        {
            if (Find(station, compactName, term) is not { } place)
            {
                return null;
            }

            typo |= place == Place.Typo;
            score += (int)place;
        }

        return score;
    }

    /// <summary>Where a term was found, from the best place to the worst.</summary>
    private enum Place
    {
        NameStart,
        NameWordStart,
        Name,
        Tags,
        Country,
        Typo,
    }

    private static Place? Find(Station station, string compactName, string term)
    {
        // "qmusic" should find "Q music" and "Q-Music", so the name is also tried without its spaces and dashes.
        if (station.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase)
            || compactName.StartsWith(term, StringComparison.OrdinalIgnoreCase))
        {
            return Place.NameStart;
        }

        if (StartsWord(station.Name, term))
        {
            return Place.NameWordStart;
        }

        if (station.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || compactName.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return Place.Name;
        }

        if (station.Tags.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return Place.Tags;
        }

        if (station.Country.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return Place.Country;
        }

        var maxEdits = MaxEdits(term);
        if (maxEdits > 0
            && (ContainsSimilarWord(station.Name, term, maxEdits)
                || ContainsSimilarWord(station.Tags, term, maxEdits)
                || ContainsSimilarWord(station.Country, term, maxEdits)
                || ContainsSimilarWord(compactName, term, maxEdits)))
        {
            return Place.Typo;
        }

        return null;
    }

    /// <summary>True when a word in <paramref name="text"/> starts with the term, like "538" in "Radio 538".</summary>
    private static bool StartsWord(string text, string term)
    {
        var i = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        while (i >= 0)
        {
            if (i == 0 || !char.IsLetterOrDigit(text[i - 1]))
            {
                return true;
            }

            i = text.IndexOf(term, i + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>Short terms get no typo tolerance: "rock" would otherwise also find "rick" and "roc".</summary>
    private static int MaxEdits(string term) => term.Length switch
    {
        < 5 => 0,
        < 8 => 1,
        _ => 2,
    };

    /// <summary>True when a word in <paramref name="text"/> starts with something within <paramref name="maxEdits"/> of the term.</summary>
    private static bool ContainsSimilarWord(string text, string term, int maxEdits)
    {
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && !char.IsLetterOrDigit(text[i])) i++;
            var start = i;
            while (i < text.Length && char.IsLetterOrDigit(text[i])) i++;

            var word = text.AsSpan(start, i - start);
            // Typos rarely hit the first letter; requiring it keeps "qmusic" from matching every "music" station.
            if (word.Length >= term.Length - maxEdits
                && char.ToLowerInvariant(word[0]) == char.ToLowerInvariant(term[0])
                && Levenshtein.PrefixDistance(term, word) <= maxEdits)
            {
                return true;
            }
        }

        return false;
    }

    private static string Compact(string value) =>
        string.Create(value.Count(char.IsLetterOrDigit), value, static (span, source) =>
        {
            var i = 0;
            foreach (var c in source)
            {
                if (char.IsLetterOrDigit(c)) span[i++] = c;
            }
        });
}
