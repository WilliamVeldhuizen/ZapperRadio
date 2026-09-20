using System.Globalization;

namespace ZapperRadio.Core.Models;

/// <summary>A song a station played, as it was heard while the station was streaming in the background.</summary>
public sealed record PlayedTrack(string Title, string StationName, string StationUrl, DateTimeOffset PlayedAt)
{
    /// <summary>Which station played it and when, e.g. "Qmusic · 21:48". The time is on the 24-hour clock in every language.</summary>
    public string Details => $"{StationName} · {PlayedAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)}";
}
