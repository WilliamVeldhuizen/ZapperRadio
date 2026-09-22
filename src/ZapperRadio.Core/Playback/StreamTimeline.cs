using ZapperRadio.Core.Audio;
using ZapperRadio.Core.Streaming;

namespace ZapperRadio.Core.Playback;

/// <summary>What a stream was doing at one moment: what it said, what it sounded like, and what that means for zapping.</summary>
public sealed record StreamMoment(IcyMetadata? Metadata, bool IsAssumedAdBreak, Sound Sound, ChannelState State)
{
    /// <summary>A stream nothing is known of yet.</summary>
    public static readonly StreamMoment Unknown = new(null, false, Sound.Unknown, ChannelState.Unknown);

    /// <summary>True during an ad break the station marks, or an assumed one.</summary>
    public bool IsInAdBreak => Metadata?.IsAd == true || IsAssumedAdBreak;

    /// <summary>Whether the moment is an ad break or talking, which is what the zapper leaves.</summary>
    public bool IsBreak => State is ChannelState.Ad or ChannelState.Speech;
}

/// <summary>
/// What a stream did over the last minutes, so the station being played from the time-shift buffer can be judged by
/// what is heard rather than by the live broadcast, which is ahead of it. A break is often known before it is
/// heard: the station announces it, or the classifier hears it at the live edge while the listener is a minute or
/// so behind. Breaks heard as speech are dated back to where the talking began (<see cref="SoundHistory.SpeechStretch"/>),
/// because the classifier needs a window or two to be sure, and the zap can then be cut at the break's boundary
/// instead of after the first seconds of it. A song is dated back to where it began, less
/// <see cref="SongPreRoll"/>, which is where a zap to it lands: from there on it is heard as the song, even though
/// the music took a few windows to be sure of.
/// </summary>
public sealed class StreamTimeline
{
    /// <summary>
    /// How much earlier than where a song is thought to begin a zap lands. The start is known to about a window of the
    /// classifier, and the tail of a jingle is better than a missing first note.
    /// </summary>
    public static readonly TimeSpan SongPreRoll = TimeSpan.FromSeconds(2);

    private readonly List<(DateTimeOffset At, StreamMoment Moment)> _entries = [];

    /// <summary>
    /// Where a moment that comes in <paramref name="now"/> starts. A break that only the sound tells about began where
    /// the talking did. A new song began where <paramref name="songStartedAt"/> says, less the pre-roll, but never
    /// during an ad break, marked or assumed, which really did come before it. Everything else, including a marked
    /// ad break, begins when it came in.
    /// </summary>
    public DateTimeOffset StartOf(StreamMoment moment, SoundHistory sound, DateTimeOffset? songStartedAt, DateTimeOffset now)
    {
        var before = Latest;
        if (moment.IsBreak && before?.IsBreak != true && moment.Metadata?.IsAd != true)
        {
            return now - sound.SpeechStretch;
        }

        if (moment.State == ChannelState.Song && (before?.State != ChannelState.Song || before.Metadata != moment.Metadata)
            && songStartedAt - SongPreRoll is { } start && start < now)
        {
            var lastAd = _entries.FindLast(e => e.Moment.IsInAdBreak);
            return lastAd.Moment is not null && start <= lastAd.At ? lastAd.At + TimeSpan.FromTicks(1) : start;
        }

        return now;
    }

    /// <summary>The newest moment recorded, or null while there is none.</summary>
    public StreamMoment? Latest => _entries.Count > 0 ? _entries[^1].Moment : null;

    /// <summary>
    /// Records what the stream does from <paramref name="at"/> on. A moment dated back replaces whatever was recorded
    /// after its start, because it is the better account of that stretch.
    /// </summary>
    public void Record(DateTimeOffset at, StreamMoment moment)
    {
        var keep = _entries.Count;
        while (keep > 0 && _entries[keep - 1].At >= at)
        {
            keep--;
        }

        _entries.RemoveRange(keep, _entries.Count - keep);
        if (Latest != moment)
        {
            _entries.Add((at, moment));
        }
    }

    /// <summary>What the stream was doing at <paramref name="time"/>, or null before anything was recorded.</summary>
    public StreamMoment? At(DateTimeOffset time)
    {
        StreamMoment? found = null;
        foreach (var (at, moment) in _entries)
        {
            if (at > time)
            {
                break;
            }

            found = moment;
        }

        return found;
    }

    /// <summary>When the stream next did something else after <paramref name="time"/>, or null when it has not since.</summary>
    public DateTimeOffset? NextChangeAfter(DateTimeOffset time)
    {
        foreach (var (at, _) in _entries)
        {
            if (at > time)
            {
                return at;
            }
        }

        return null;
    }

    /// <summary>Forgets what happened before <paramref name="time"/>, keeping the moment that was going on then.</summary>
    public void Prune(DateTimeOffset time)
    {
        var drop = 0;
        while (drop + 1 < _entries.Count && _entries[drop + 1].At <= time)
        {
            drop++;
        }

        _entries.RemoveRange(0, drop);
    }
}
