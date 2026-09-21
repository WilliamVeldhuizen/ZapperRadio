using ZapperRadio.Core.Audio;

namespace ZapperRadio.Core.Tests;

public class SongClockTests
{
    private static readonly DateTimeOffset TitleAt = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Length = TimeSpan.FromMinutes(3);

    /// <summary>A history from letters as in the recorded streams: M = music, S = speech, anything else unknown.</summary>
    private static SoundHistory Heard(string windows)
    {
        var history = new SoundHistory();
        foreach (var window in windows)
        {
            history.Add(window switch { 'M' => Sound.Music, 'S' => Sound.Speech, _ => Sound.Unknown });
        }

        return history;
    }

    private static SongClock Started()
    {
        var clock = new SongClock();
        clock.Start(TitleAt);
        clock.SetLength(Length);
        return clock;
    }

    [Fact]
    public void SaysNothingBeforeTheSongIsHeard()
    {
        var clock = Started();

        Assert.False(clock.Update(Heard("S"), TitleAt + TimeSpan.FromSeconds(5)));
        Assert.Null(clock.StartedAt);
        Assert.Null(clock.OverdueAt);
    }

    [Fact]
    public void StartsTheSongAtTheMusicThatFollowsTheTitle()
    {
        var clock = Started();

        // The title came in over the jingle: 15 seconds of speech, then the song.
        Assert.False(clock.Update(Heard("SSS"), TitleAt + TimeSpan.FromSeconds(15)));
        Assert.False(clock.Update(Heard("SSSM"), TitleAt + TimeSpan.FromSeconds(20)));
        Assert.True(clock.Update(Heard("SSSMM"), TitleAt + TimeSpan.FromSeconds(25)));

        var started = TitleAt + TimeSpan.FromSeconds(15);
        Assert.Equal(started, clock.StartedAt);
        Assert.Equal(started + Length + SongClock.Overrun, clock.OverdueAt);
    }

    [Fact]
    public void ATitleThatArrivesLateDoesNotMoveTheSongForward()
    {
        var clock = Started();

        // The song was already playing when its title came in, so it can only have started earlier, not later.
        Assert.True(clock.Update(Heard("MMMMMM"), TitleAt + TimeSpan.FromSeconds(5)));

        Assert.Equal(TitleAt, clock.StartedAt);
    }

    [Fact]
    public void AnchorsOnlyOnce()
    {
        var clock = Started();
        clock.Update(Heard("MM"), TitleAt + TimeSpan.FromSeconds(10));
        var started = clock.StartedAt;

        Assert.False(clock.Update(Heard("SSSSSS"), TitleAt + TimeSpan.FromMinutes(2)));

        Assert.Equal(started, clock.StartedAt);
    }

    [Fact]
    public void FallsBackToTheTitleWhenNothingRecognizableIsHeard()
    {
        var clock = Started();

        Assert.False(clock.Update(Heard("---"), TitleAt + SongClock.MaxWait - TimeSpan.FromSeconds(5)));
        Assert.True(clock.Update(Heard("----"), TitleAt + SongClock.MaxWait));

        Assert.Equal(TitleAt, clock.StartedAt);
    }

    [Fact]
    public void AStreamThatIsNotListenedToIsTimedFromItsTitle()
    {
        var clock = Started();

        Assert.True(clock.Update(null, TitleAt + TimeSpan.FromSeconds(1)));

        Assert.Equal(TitleAt, clock.StartedAt);
        Assert.Equal(TitleAt + Length + SongClock.Overrun, clock.OverdueAt);
    }

    [Fact]
    public void WaitsForTheLengthOfTheSong()
    {
        var clock = new SongClock();
        clock.Start(TitleAt);
        clock.Update(null, TitleAt);

        Assert.Equal(TitleAt, clock.StartedAt);
        Assert.Null(clock.OverdueAt);

        clock.SetLength(Length);
        Assert.Equal(TitleAt + Length + SongClock.Overrun, clock.OverdueAt);
    }

    [Fact]
    public void StopForgetsTheSong()
    {
        var clock = Started();
        clock.Update(null, TitleAt);

        clock.Stop();

        Assert.False(clock.IsRunning);
        Assert.Null(clock.StartedAt);
        Assert.Null(clock.OverdueAt);
        Assert.False(clock.Update(Heard("MMMM"), TitleAt + TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void BeganAt_IsTheTitleUntilTheAudioSaysBetter()
    {
        var clock = Started();
        Assert.Equal(TitleAt, clock.BeganAt);

        clock.Update(Heard("SSSMM"), TitleAt + TimeSpan.FromSeconds(25));
        Assert.Equal(TitleAt + TimeSpan.FromSeconds(15), clock.BeganAt);

        clock.Stop();
        Assert.Null(clock.BeganAt);
    }
}
