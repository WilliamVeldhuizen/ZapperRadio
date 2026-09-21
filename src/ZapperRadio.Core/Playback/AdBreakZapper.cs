using ZapperRadio.Core.Audio;

namespace ZapperRadio.Core.Playback;

/// <summary>What a station stream is doing right now, as far as zapping is concerned.</summary>
public enum ChannelState
{
    /// <summary>Not playing: connecting, buffering, reconnecting or failed.</summary>
    Unavailable,

    /// <summary>In an ad break, marked by the station or assumed from an overdue song.</summary>
    Ad,

    /// <summary>Someone talks: a presenter, the news or a talk show. For listening to music only, a break like an ad.</summary>
    Speech,

    /// <summary>Playing, but it is unknown what: no title and no music or speech heard.</summary>
    Unknown,

    /// <summary>Playing a song: music heard, or a title that is not an ad while nothing is heard.</summary>
    Song,
}

public readonly record struct Channel(string Url, ChannelState State)
{
    /// <summary>
    /// A title alone does not prove that a song is playing: some stations send their own name or a program name,
    /// and presenters talk between songs, so only a title shaped like "Artist - Title" counts here. What the stream
    /// sounds like weighs first either way, and music counts as a song even without a title.
    /// </summary>
    public static ChannelState StateOf(bool isInAdBreak, bool isLive, bool hasSongTitle, Sound sound) =>
        isInAdBreak ? ChannelState.Ad
        : !isLive ? ChannelState.Unavailable
        : sound == Sound.Speech ? ChannelState.Speech
        : sound == Sound.Music || hasSongTitle ? ChannelState.Song
        : ChannelState.Unknown;
}

/// <summary>
/// Decides when to zap away from an ad break or speech and when to zap back. When the station being listened to
/// starts an ad break or someone talks, it zaps to the highest favorite that plays a song (or else the highest one
/// that at least plays something). It zaps back once the station zapped away from plays a song again, but never in
/// the middle of one: as long as the station it landed on plays music, that music is what you came for, so the
/// return waits for that station's own break. Picking a station yourself ends the zapping: that station stays on,
/// even if it is in a break right then.
/// Two rules shape that: the favorites in <see cref="NeverZapTo"/> are never landed on, and without
/// <see cref="ReturnAfterBreak"/> the station it landed on stays on instead of the zapper going back.
/// </summary>
public sealed class AdBreakZapper
{
    /// <summary>
    /// How long the station zapped away from is watched for. Ad breaks rarely last this long, and by the time it
    /// has passed you have been listening to the station you ended up on for a while, so going back to another one
    /// would be the surprise rather than the relief.
    /// </summary>
    public static readonly TimeSpan MaxAdBreak = TimeSpan.FromMinutes(10);

    private DateTimeOffset _zappedAt;
    private bool _returnAfterBreak = true;

    /// <summary>
    /// Favorites a break is never zapped to, such as a news station, which is no place to wait for the music. They
    /// are still zapped away from, and still returned to after a break when that is where the zapping started.
    /// </summary>
    public IReadOnlySet<string> NeverZapTo { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Whether it goes back to the station a break was zapped away from once that plays a song again. Without it the
    /// station it landed on stays on, until that one reaches a break of its own.
    /// </summary>
    public bool ReturnAfterBreak
    {
        get => _returnAfterBreak;
        set
        {
            _returnAfterBreak = value;
            if (!value)
            {
                ZappedFrom = null;
            }
        }
    }

    /// <summary>The station a break was zapped away from, which is returned to when it plays a song again.</summary>
    public string? ZappedFrom { get; private set; }

    /// <summary>A station picked during its ad break or speech, whose break is therefore not zapped away from.</summary>
    public string? KeptDuringBreak { get; private set; }

    /// <summary>Call when you pick a station yourself.</summary>
    public void OnPicked(string url, ChannelState state)
    {
        ZappedFrom = null;
        KeptDuringBreak = IsBreak(state) ? url : null;
    }

    public void OnStopped()
    {
        ZappedFrom = null;
        KeptDuringBreak = null;
    }

    /// <summary>
    /// Returns the station to zap to, or null to stay. Call it whenever a stream changes.
    /// </summary>
    /// <param name="active">The station being listened to.</param>
    /// <param name="favorites">The favorites in list order; only these can be zapped to, because their streams stay open.</param>
    public string? Next(Channel active, IReadOnlyList<Channel> favorites, DateTimeOffset now)
    {
        if (KeptDuringBreak is { } kept && !IsBreak(StateOf(kept, active, favorites)))
        {
            KeptDuringBreak = null;
        }

        if (ZappedFrom is { } origin)
        {
            if (origin == active.Url || now - _zappedAt > MaxAdBreak || !favorites.Any(f => f.Url == origin))
            {
                ZappedFrom = null;
            }
            else if (StateOf(origin, active, favorites) == ChannelState.Song && active.State != ChannelState.Song)
            {
                // The station it was zapped to has reached its own break, so going back interrupts nothing.
                ZappedFrom = null;
                return origin;
            }
        }

        if (!IsBreak(active.State) || active.Url == KeptDuringBreak)
        {
            return null;
        }

        var next = favorites.FirstOrDefault(f => CanLandOn(f, active) && f.State == ChannelState.Song).Url
                   ?? favorites.FirstOrDefault(f => CanLandOn(f, active) && f.State == ChannelState.Unknown).Url;
        if (next is not null && ZappedFrom is null && ReturnAfterBreak)
        {
            ZappedFrom = active.Url;
            _zappedAt = now;
        }

        return next;
    }

    private bool CanLandOn(Channel favorite, Channel active) => favorite.Url != active.Url && !NeverZapTo.Contains(favorite.Url);

    private static bool IsBreak(ChannelState state) => state is ChannelState.Ad or ChannelState.Speech;

    private static ChannelState StateOf(string url, Channel active, IReadOnlyList<Channel> favorites) =>
        url == active.Url ? active.State : favorites.FirstOrDefault(f => f.Url == url) is { Url: not null } found ? found.State : ChannelState.Unavailable;
}
