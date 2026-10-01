using ZapperRadio.Core.Playback;

namespace ZapperRadio.Core.Tests;

public class BreakClockTests
{
    private static readonly DateTimeOffset Midnight = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A station heard for <paramref name="hours"/> hours, in 10-second steps, breaking at the given minutes of every hour.</summary>
    private static BreakClock Heard(int hours, params int[] breakMinutes)
    {
        var clock = new BreakClock();
        var step = TimeSpan.FromSeconds(10);
        for (var at = Midnight; at < Midnight.AddHours(hours); at += step)
        {
            clock.Record(at, step, breakMinutes.Contains(at.Minute));
        }

        return clock;
    }

    [Fact]
    public void IsNotUsedUntilTheStationWasHeardForHours()
    {
        var clock = Heard(3, 0, 1, 2);

        Assert.False(clock.IsLearned);
        Assert.False(clock.IsBreakMinute(0));
        Assert.Empty(clock.UsualBreaks(TimeSpan.Zero));
    }

    [Fact]
    public void FindsTheMinutesTheStationUsuallyBreaksAt()
    {
        var clock = Heard(8, 0, 1, 2, 30, 31);

        Assert.True(clock.IsLearned);
        Assert.True(clock.IsBreakMinute(1));
        Assert.True(clock.IsBreakMinute(31));
        Assert.False(clock.IsBreakMinute(15));
        Assert.Equal([(0, 3), (30, 2)], clock.UsualBreaks(TimeSpan.Zero));
    }

    [Fact]
    public void KeepsABreakAcrossTheHourInOnePiece()
    {
        var clock = Heard(8, 58, 59, 0, 1);

        Assert.Equal([(58, 4)], clock.UsualBreaks(TimeSpan.Zero));
    }

    [Fact]
    public void IgnoresABreakThatCameOnlyOnce()
    {
        var clock = Heard(8);
        var step = TimeSpan.FromSeconds(10);
        for (var at = Midnight.AddHours(8).AddMinutes(20); at < Midnight.AddHours(8).AddMinutes(23); at += step)
        {
            clock.Record(at, step, isBreak: true);
        }

        Assert.Empty(clock.UsualBreaks(TimeSpan.Zero));
    }

    [Fact]
    public void SaysABreakIsDueAFewMinutesAhead()
    {
        var clock = Heard(8, 0, 1, 2);

        Assert.True(clock.IsBreakDue(Midnight.AddHours(9).AddMinutes(-2)));
        Assert.True(clock.IsBreakDue(Midnight.AddHours(9).AddMinutes(1)));
        Assert.False(clock.IsBreakDue(Midnight.AddHours(9).AddMinutes(10)));
        Assert.False(clock.IsBreakDue(Midnight.AddHours(9).AddMinutes(-5)));
    }

    [Fact]
    public void ShowsTheMinutesOnTheLocalClock()
    {
        // On the hour in UTC is half past in India, five and a half hours ahead.
        var clock = Heard(8, 0, 1);
        var india = TimeSpan.FromHours(5.5);

        Assert.Equal([(30, 2)], clock.UsualBreaks(india));
        Assert.Equal(1.0, clock.Shares(india)[30]);
        Assert.Equal(0.0, clock.Shares(india)[0]);
        Assert.Equal([(0, 2)], clock.UsualBreaks(TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void FollowsAStationThatChangesItsClock()
    {
        var clock = Heard(8, 0, 1);
        var step = TimeSpan.FromSeconds(10);
        var start = Midnight.AddHours(8);
        for (var at = start; at < start.AddHours(200); at += step)
        {
            clock.Record(at, step, at.Minute is 20 or 21);
        }

        Assert.Equal([(20, 2)], clock.UsualBreaks(TimeSpan.Zero));
    }

    [Fact]
    public void SurvivesBeingSavedAndLoaded()
    {
        var clock = BreakClock.FromData(Heard(8, 0, 30).ToData());

        Assert.Equal([(0, 1), (30, 1)], clock.UsualBreaks(TimeSpan.Zero));
    }

    [Fact]
    public void StartsEmptyFromDataThatDoesNotFit()
    {
        var clock = BreakClock.FromData(new BreakClockData([1, 2, 3], [1]));

        Assert.Equal(0, clock.HoursHeard);
    }

    [Fact]
    public void StoreKeepsTheClocksOfEveryStation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"break-clocks-{Guid.NewGuid():N}.json");
        try
        {
            var store = new BreakClockStore(path);
            store.Save(new Dictionary<string, BreakClock> { ["http://a"] = Heard(8, 0, 1) });

            var loaded = store.Load();

            Assert.Equal([(0, 2)], loaded["http://a"].UsualBreaks(TimeSpan.Zero));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
