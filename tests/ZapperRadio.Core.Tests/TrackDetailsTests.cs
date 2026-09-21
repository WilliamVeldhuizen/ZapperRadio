using System.Globalization;
using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Tests;

/// <summary>The line under a song names the station and when it was heard, written the way the language of the app writes dates.</summary>
public class TrackDetailsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 17, 21, 48, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("en-US")]
    [InlineData("nl-NL")]
    [InlineData("ja-JP")]
    public void FavoriteTrack_WritesTheDateInTheCurrentCulture(string culture)
    {
        var details = InCulture(culture, () => new FavoriteTrack("Queen - Bohemian Rhapsody", "Radio 2", Moment).Details);

        Assert.Equal($"Radio 2 · {Moment.ToLocalTime().ToString("d", CultureInfo.GetCultureInfo(culture))}", details);
    }

    [Fact]
    public void FavoriteTrack_DiffersBetweenLanguages()
    {
        var track = new FavoriteTrack("Queen - Bohemian Rhapsody", "Radio 2", Moment);

        Assert.NotEqual(InCulture("en-US", () => track.Details), InCulture("nl-NL", () => track.Details));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("nl-NL")]
    [InlineData("ja-JP")]
    public void PlayedTrack_KeepsTheTwentyFourHourClockInEveryCulture(string culture)
    {
        var details = InCulture(culture, () => new PlayedTrack("Queen - Bohemian Rhapsody", "Radio 2", "http://radio2", Moment).Details);

        Assert.Equal($"Radio 2 · {Moment.ToLocalTime():HH:mm}", details);
    }

    /// <summary>Runs <paramref name="read"/> with the culture switched, and puts the previous one back for the tests that follow.</summary>
    private static string InCulture(string culture, Func<string> read)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return read();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
