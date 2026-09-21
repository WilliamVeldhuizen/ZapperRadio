using ZapperRadio.Core.Playback;

namespace ZapperRadio.Core.Tests;

public class AdBreakZapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    private static Channel Song(string url) => new(url, ChannelState.Song);

    private static Channel Ad(string url) => new(url, ChannelState.Ad);

    private static Channel Speech(string url) => new(url, ChannelState.Speech);

    private static Channel Unknown(string url) => new(url, ChannelState.Unknown);

    private static Channel Unavailable(string url) => new(url, ChannelState.Unavailable);

    [Fact]
    public void StaysWhileTheStationPlaysASong()
    {
        var zapper = new AdBreakZapper();

        Assert.Null(zapper.Next(Song("a"), [Song("a"), Song("b")], Now));
    }

    [Fact]
    public void ZapsToTheHighestFavoritePlayingASong()
    {
        var zapper = new AdBreakZapper();

        var next = zapper.Next(Ad("a"), [Ad("a"), Unavailable("b"), Unknown("c"), Ad("d"), Song("e"), Song("f")], Now);

        Assert.Equal("e", next);
        Assert.Equal("a", zapper.ZappedFrom);
    }

    [Fact]
    public void ZapsToAFavoriteWithoutTitlesWhenNoneIsPlayingASong()
    {
        var zapper = new AdBreakZapper();

        Assert.Equal("c", zapper.Next(Ad("a"), [Ad("a"), Ad("b"), Unknown("c")], Now));
    }

    [Fact]
    public void WaitsUntilAFavoriteIsAvailable()
    {
        var zapper = new AdBreakZapper();

        Assert.Null(zapper.Next(Ad("a"), [Ad("a"), Ad("b"), Unavailable("c")], Now));
        Assert.Equal("b", zapper.Next(Ad("a"), [Ad("a"), Song("b"), Unavailable("c")], Now.AddSeconds(20)));
    }

    [Fact]
    public void ZapsBackWhenTheAdBreakIsOver()
    {
        var zapper = new AdBreakZapper();
        zapper.Next(Ad("a"), [Ad("a"), Song("b")], Now);

        Assert.Null(zapper.Next(Song("b"), [Ad("a"), Song("b")], Now.AddMinutes(1)));
        // Between the ads and the next song, some stations send an empty title.
        Assert.Null(zapper.Next(Song("b"), [Unknown("a"), Song("b")], Now.AddMinutes(2)));
        Assert.Equal("a", zapper.Next(Ad("b"), [Song("a"), Ad("b")], Now.AddMinutes(3)));
        Assert.Null(zapper.ZappedFrom);
        Assert.Null(zapper.Next(Song("a"), [Song("a"), Song("b")], Now.AddMinutes(3)));
    }

    [Fact]
    public void DoesNotZapBackWhileTheStationItLandedOnPlaysASong()
    {
        var zapper = new AdBreakZapper();
        zapper.Next(Ad("a"), [Ad("a"), Song("b")], Now);

        // The break on "a" is over, but "b" is halfway through a song: going back now would cut it off.
        Assert.Null(zapper.Next(Song("b"), [Song("a"), Song("b")], Now.AddMinutes(1)));
        Assert.Null(zapper.Next(Song("b"), [Song("a"), Song("b")], Now.AddMinutes(2)));
        Assert.Equal("a", zapper.ZappedFrom);
    }

    [Fact]
    public void ZapsBackOnceTheSongItLandedOnIsOver()
    {
        var zapper = new AdBreakZapper();
        zapper.Next(Ad("a"), [Ad("a"), Song("b")], Now);

        Assert.Null(zapper.Next(Song("b"), [Song("a"), Song("b")], Now.AddMinutes(1)));
        // "b" starts talking, which is the moment to go back to the station that was picked.
        Assert.Equal("a", zapper.Next(Speech("b"), [Song("a"), Speech("b")], Now.AddMinutes(2)));
        Assert.Null(zapper.ZappedFrom);
    }

    [Fact]
    public void DoesNotZapBackToAStationThatIsInABreakItself()
    {
        var zapper = new AdBreakZapper();
        zapper.Next(Ad("a"), [Ad("a"), Song("b"), Song("c")], Now);

        // "b" is done with its song, but "a" is back in the ads, so it zaps on instead of back.
        Assert.Equal("c", zapper.Next(Ad("b"), [Ad("a"), Ad("b"), Song("c")], Now.AddMinutes(1)));
        Assert.Equal("a", zapper.ZappedFrom);
    }

    [Fact]
    public void ZapsAwayFromSpeech_ToMusicOnly_AndBackWhenTheMusicStarts()
    {
        var zapper = new AdBreakZapper();

        Assert.Equal("c", zapper.Next(Speech("a"), [Speech("a"), Speech("b"), Song("c")], Now));
        Assert.Null(zapper.Next(Song("c"), [Speech("a"), Speech("b"), Song("c")], Now.AddMinutes(1)));
        Assert.Equal("a", zapper.Next(Speech("c"), [Song("a"), Speech("b"), Speech("c")], Now.AddMinutes(2)));
    }

    [Fact]
    public void ZapsOnWhenTheNextStationStartsAnAdBreakToo_AndStillReturnsToTheFirst()
    {
        var zapper = new AdBreakZapper();
        zapper.Next(Ad("a"), [Ad("a"), Song("b"), Song("c")], Now);

        Assert.Equal("c", zapper.Next(Ad("b"), [Ad("a"), Ad("b"), Song("c")], Now.AddMinutes(1)));
        Assert.Equal("a", zapper.Next(Ad("c"), [Song("a"), Ad("b"), Ad("c")], Now.AddMinutes(2)));
    }

    [Fact]
    public void DoesNotZapBackAfterAVeryLongBreak()
    {
        var zapper = new AdBreakZapper();
        zapper.Next(Ad("a"), [Ad("a"), Song("b")], Now);

        // One song after another on "b", so the moment to return never comes and the break is given up on.
        Assert.Null(zapper.Next(Song("b"), [Song("a"), Song("b")], Now.AddMinutes(1)));
        Assert.Null(zapper.Next(Song("b"), [Song("a"), Song("b")], Now + AdBreakZapper.MaxAdBreak + TimeSpan.FromSeconds(1)));
        Assert.Null(zapper.ZappedFrom);
    }

    [Fact]
    public void DoesNotZapBackToARemovedFavorite()
    {
        var zapper = new AdBreakZapper();
        zapper.Next(Ad("a"), [Ad("a"), Song("b")], Now);

        Assert.Null(zapper.Next(Song("b"), [Song("b")], Now.AddMinutes(1)));
        Assert.Null(zapper.ZappedFrom);
    }

    [Fact]
    public void PickingAStationEndsTheZapping()
    {
        var zapper = new AdBreakZapper();
        zapper.Next(Ad("a"), [Ad("a"), Song("b"), Song("c")], Now);

        zapper.OnPicked("c", ChannelState.Song);

        Assert.Null(zapper.Next(Song("c"), [Song("a"), Song("b"), Song("c")], Now.AddMinutes(1)));
    }

    [Fact]
    public void KeepsAStationPickedDuringItsAdBreak_UntilThatBreakIsOver()
    {
        var zapper = new AdBreakZapper();
        zapper.OnPicked("a", ChannelState.Speech);

        Assert.Null(zapper.Next(Speech("a"), [Speech("a"), Song("b")], Now));
        // The news is followed by ads: still the same break.
        Assert.Null(zapper.Next(Ad("a"), [Ad("a"), Song("b")], Now));
        Assert.Null(zapper.Next(Song("a"), [Song("a"), Song("b")], Now.AddMinutes(1)));
        Assert.Equal("b", zapper.Next(Ad("a"), [Ad("a"), Song("b")], Now.AddMinutes(20)));
    }

    [Fact]
    public void NeverZapsToAFavoriteThatIsLeftOut()
    {
        var zapper = new AdBreakZapper { NeverZapTo = new HashSet<string> { "b", "c" } };

        Assert.Equal("d", zapper.Next(Ad("a"), [Ad("a"), Song("b"), Unknown("c"), Song("d")], Now));
    }

    [Fact]
    public void WaitsWhenOnlyFavoritesThatAreLeftOutPlayASong()
    {
        var zapper = new AdBreakZapper { NeverZapTo = new HashSet<string> { "b" } };

        Assert.Null(zapper.Next(Ad("a"), [Ad("a"), Song("b")], Now));
        Assert.Null(zapper.ZappedFrom);
    }

    [Fact]
    public void StillZapsAwayFromAndBackToAFavoriteThatIsLeftOut()
    {
        var zapper = new AdBreakZapper { NeverZapTo = new HashSet<string> { "a" } };

        Assert.Equal("b", zapper.Next(Speech("a"), [Speech("a"), Song("b")], Now));
        Assert.Equal("a", zapper.Next(Ad("b"), [Song("a"), Ad("b")], Now.AddMinutes(2)));
    }

    [Fact]
    public void StaysWhereItLanded_WhenItDoesNotReturnAfterABreak()
    {
        var zapper = new AdBreakZapper { ReturnAfterBreak = false };

        Assert.Equal("b", zapper.Next(Ad("a"), [Ad("a"), Song("b")], Now));
        Assert.Null(zapper.ZappedFrom);
        Assert.Null(zapper.Next(Song("b"), [Song("a"), Song("b")], Now.AddMinutes(1)));
        // Its own break is zapped away from as usual, which may well be back to where it came from.
        Assert.Equal("a", zapper.Next(Ad("b"), [Song("a"), Ad("b")], Now.AddMinutes(2)));
    }

    [Fact]
    public void TurningTheReturnOffForgetsWhereItCameFrom()
    {
        var zapper = new AdBreakZapper();
        Assert.Equal("b", zapper.Next(Ad("a"), [Unavailable("c"), Ad("a"), Song("b")], Now));

        zapper.ReturnAfterBreak = false;

        Assert.Null(zapper.ZappedFrom);
        // A plain zap to the highest favorite playing a song, rather than a return to "a".
        Assert.Equal("c", zapper.Next(Ad("b"), [Song("c"), Song("a"), Ad("b")], Now.AddMinutes(1)));
    }

    [Fact]
    public void ZapsAwayFromAStationThatIsNotAFavorite_ButNotBack()
    {
        var zapper = new AdBreakZapper();

        Assert.Equal("b", zapper.Next(Ad("x"), [Song("b")], Now));
        Assert.Null(zapper.Next(Song("b"), [Song("b")], Now.AddMinutes(1)));
    }
}
