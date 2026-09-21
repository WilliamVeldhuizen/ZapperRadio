namespace ZapperRadio.Core.Audio;

/// <summary>
/// Decides when the song a stream title announced really started, and when it should therefore have ended.
/// Plenty of stations announce the next title while the previous song is still fading, or over the jingle in
/// between, so a title can run 10 to 20 seconds ahead of the audio. Timing the song from its title then marks it
/// overdue while it is still playing, and the first presenter or station ident after it is enough to call an ad
/// break that is not one. The audio says when the song really starts, so the clock is anchored to the first music
/// heard after the title. A stream that is not listened to, or that sounds like neither music nor speech for a
/// while, falls back to the title, as the clock used to work for every stream.
/// </summary>
public sealed class SongClock
{
    /// <summary>Windows of music in a row (about 10 seconds) that anchor the clock; one window alone is noise.</summary>
    public const int MusicWindowsToStart = 2;

    /// <summary>How much of the audio one classified window covers.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the music is waited for before the title is timed from after all. Instrumental intros, applause
    /// and quiet passages sound like neither music nor speech, and must not hold the clock forever.
    /// </summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(45);

    /// <summary>
    /// How long a song may run past its length before it counts as overdue. It covers the seconds between the
    /// music the clock anchors to and the real first note, a longer version than the one that was looked up, and
    /// a short announcement after the song. It can be this short because the clock starts with the song itself;
    /// timing from the title needed 30 seconds, most of which went on the title's head start.
    /// </summary>
    public static readonly TimeSpan Overrun = TimeSpan.FromSeconds(20);

    private DateTimeOffset _titleAt;
    private TimeSpan? _length;

    /// <summary>When the song started, or null while the audio has not said yet.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>Whether a song is being timed at all.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// When the song began as well as it is known now: where the audio says it started, or else when its title came
    /// in. Null while no song is being timed.
    /// </summary>
    public DateTimeOffset? BeganAt => IsRunning ? StartedAt ?? _titleAt : null;

    /// <summary>When the song is overdue, or null while its start or its length is unknown.</summary>
    public DateTimeOffset? OverdueAt =>
        IsRunning && StartedAt is { } started && _length is { } length ? started + length + Overrun : null;

    /// <summary>Call when a station sends the title of a song.</summary>
    public void Start(DateTimeOffset titleAt)
    {
        IsRunning = true;
        StartedAt = null;
        _length = null;
        _titleAt = titleAt;
    }

    /// <summary>Call once the length of the song has been looked up.</summary>
    public void SetLength(TimeSpan length) => _length = length;

    /// <summary>Call when the song ends, or when there is no song to time.</summary>
    public void Stop()
    {
        IsRunning = false;
        StartedAt = null;
        _length = null;
    }

    /// <summary>
    /// Anchors the clock to the audio. Call it for every classified window, and for a stream that is not
    /// listened to whenever it comes up.
    /// </summary>
    /// <param name="sound">What the stream sounds like, or null when it is not listened to.</param>
    /// <returns>Whether the song was anchored by this call, so <see cref="OverdueAt"/> has to be waited for anew.</returns>
    public bool Update(SoundHistory? sound, DateTimeOffset now)
    {
        if (!IsRunning || StartedAt is not null)
        {
            return false;
        }

        if (sound is { ConsecutiveMusic: >= MusicWindowsToStart } music)
        {
            // The run of music has been playing since it began, and that is where the song started, unless it was
            // already playing when the title came in: a title that arrives late cannot make the song start later.
            var began = now - music.ConsecutiveMusic * Window;
            StartedAt = began > _titleAt ? began : _titleAt;
        }
        else if (sound is null || now - _titleAt >= MaxWait)
        {
            StartedAt = _titleAt;
        }

        return StartedAt is not null;
    }
}
