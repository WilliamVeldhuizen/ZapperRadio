using System.Globalization;

namespace ZapperRadio.Core.Models;

/// <summary>A song saved while a station played it, as the station titled it (usually "Artist - Title").</summary>
public sealed record FavoriteTrack(string Title, string StationName, DateTimeOffset SavedAt)
{
    /// <summary>Where and when the song was heard, e.g. "Qmusic · 9/17/2026", with the date written the way the current culture does.</summary>
    public string Details => $"{StationName} · {SavedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}";

    /// <summary>Stations differ in capitals and spacing, so the same song is recognized regardless.</summary>
    public bool IsSameSong(string title) =>
        string.Equals(Normalize(Title), Normalize(title), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string title) =>
        string.Join(' ', title.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
