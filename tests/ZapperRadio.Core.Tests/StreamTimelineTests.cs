using ZapperRadio.Core.Audio;
using ZapperRadio.Core.Playback;
using ZapperRadio.Core.Streaming;

namespace ZapperRadio.Core.Tests;

public class StreamTimelineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly StreamMoment Song = new(new IcyMetadata("Artist - Title", false), false, Sound.Music, ChannelState.Song);
    private static readonly StreamMoment Talk = new(new IcyMetadata("Artist - Title", false), false, Sound.Speech, ChannelState.Speech);
    private static readonly StreamMoment Ad = new(new IcyMetadata(null, true), false, Sound.Unknown, ChannelState.Ad);
    private static readonly StreamMoment Next = new(new IcyMetadata("Other - Song", false), false, Sound.Music, ChannelState.Song);

    private static SoundHistory Heard(string windows)
    {
        var history = new SoundHistory();
        foreach (var window in windows)
        {
            history.Add(window switch { 'M' => Sound.Music, 'S' => Sound.Speech, _ => Sound.Unknown });
        }

        return history;
    }

    [Fact]
    public void TellsWhatTheStreamDidAtAMoment()
    {
        var timeline = new StreamTimeline();
        timeline.Record(T0, Song);
        timeline.Record(T0.AddMinutes(3), Ad);
        timeline.Record(T0.AddMinutes(5), Next);

        Assert.Null(timeline.At(T0.AddSeconds(-1)));
        Assert.Equal(Song, timeline.At(T0.AddMinutes(2)));
        Assert.Equal(Ad, timeline.At(T0.AddMinutes(3)));
        Assert.Equal(Next, timeline.At(T0.AddMinutes(9)));
        Assert.Equal(Next, timeline.Latest);
        Assert.Equal(T0.AddMinutes(3), timeline.NextChangeAfter(T0.AddMinutes(1)));
        Assert.Null(timeline.NextChangeAfter(T0.AddMinutes(5)));
    }

    [Fact]
    public void AMomentDatedBackReplacesWhatCameAfterItsStart()
    {
        var timeline = new StreamTimeline();
        timeline.Record(T0, Song);
        timeline.Record(T0.AddSeconds(200), Song with { Sound = Sound.Unknown, State = ChannelState.Song });

        // The talking was only sure at 210 seconds, but began at 195.
        timeline.Record(T0.AddSeconds(195), Talk);

        Assert.Equal(Song, timeline.At(T0.AddSeconds(194)));
        Assert.Equal(Talk, timeline.At(T0.AddSeconds(200)));
        Assert.Equal(T0.AddSeconds(195), timeline.NextChangeAfter(T0));
    }

    [Fact]
    public void RecordsOnlyChanges()
    {
        var timeline = new StreamTimeline();
        timeline.Record(T0, Song);
        timeline.Record(T0.AddSeconds(5), Song);

        Assert.Null(timeline.NextChangeAfter(T0));
    }

    [Fact]
    public void PruneKeepsTheMomentThatWasGoingOn()
    {
        var timeline = new StreamTimeline();
        timeline.Record(T0, Song);
        timeline.Record(T0.AddMinutes(3), Ad);
        timeline.Record(T0.AddMinutes(5), Next);

        timeline.Prune(T0.AddMinutes(4));

        Assert.Null(timeline.At(T0.AddMinutes(2)));
        Assert.Equal(Ad, timeline.At(T0.AddMinutes(4)));
        Assert.Equal(Next, timeline.At(T0.AddMinutes(6)));
    }

    /// <summary>A timeline whose latest moment is <paramref name="moment"/>, from a minute before <paramref name="now"/>.</summary>
    private static StreamTimeline After(StreamMoment moment, DateTimeOffset now)
    {
        var timeline = new StreamTimeline();
        timeline.Record(now.AddMinutes(-1), moment);
        return timeline;
    }

    [Fact]
    public void ABreakHeardAsSpeechStartsWhereTheTalkingBegan()
    {
        var now = T0.AddMinutes(3);

        Assert.Equal(now - 2 * SongClock.Window, After(Song, now).StartOf(Talk, Heard("MMMMSS"), null, now));
        // An assumed ad break is heard the same way.
        var assumed = new StreamMoment(Song.Metadata, true, Sound.Speech, ChannelState.Ad);
        Assert.Equal(now - 3 * SongClock.Window, After(Song, now).StartOf(assumed, Heard("MMMSuS"), null, now));
    }

    [Fact]
    public void ASongStartsWhereItBeganLessThePreRoll()
    {
        var now = T0.AddMinutes(3);
        var began = now.AddSeconds(-15);

        Assert.Equal(began - StreamTimeline.SongPreRoll, After(Talk, now).StartOf(Next, Heard("SSMMM"), began, now));
        // A new title on a station that was already playing a song.
        Assert.Equal(began - StreamTimeline.SongPreRoll, After(Song, now).StartOf(Next, Heard("MMM"), began, now));
        // Only more of the same song: nothing to date back.
        Assert.Equal(now, After(Song, now).StartOf(Song with { Sound = Sound.Unknown }, Heard("MMu"), began, now));
        // Where the song began cannot be told.
        Assert.Equal(now, After(Talk, now).StartOf(Next, Heard("SSMMM"), null, now));
    }

    [Fact]
    public void ASongNeverStartsDuringAMarkedAdBreak()
    {
        var now = T0.AddMinutes(3);
        var timeline = new StreamTimeline();
        timeline.Record(now.AddSeconds(-1), Ad);

        var start = timeline.StartOf(Next, Heard(""), now, now);
        timeline.Record(start, Next);

        Assert.True(start > now.AddSeconds(-1));
        Assert.Equal(Ad, timeline.At(now.AddSeconds(-1)));
        Assert.Equal(Next, timeline.At(now));
    }

    [Fact]
    public void EverythingElseStartsWhenItCameIn()
    {
        var now = T0.AddMinutes(3);

        // A break the station marks in its titles, which comes in with the audio it belongs to.
        Assert.Equal(now, After(Song, now).StartOf(Ad, Heard("MMMMSS"), null, now));
        // A break that is already going on does not start again.
        Assert.Equal(now, After(Talk, now).StartOf(Talk with { Sound = Sound.Unknown }, Heard("MMMMSS"), null, now));
    }
}
