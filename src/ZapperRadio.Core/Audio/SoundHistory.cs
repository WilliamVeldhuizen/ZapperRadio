namespace ZapperRadio.Core.Audio;

/// <summary>What a stream sounds like, as heard by the sound classifier.</summary>
public enum Sound
{
    /// <summary>Not heard yet, silent, or a mix that is neither clearly music nor speech.</summary>
    Unknown,

    Music,

    Speech,
}

/// <summary>
/// One classified window of a stream: what it sounded like, and how loud it was in LUFS. The loudness is only
/// measured while the window is music, because what a station does to its music is what makes it louder than the
/// next station; ads and talk are mixed at a level of their own. <paramref name="SpeechFrom"/> is how far into a
/// window of speech the talking begins, as a fraction of the window: 0 when it talks throughout, more when the song
/// was still playing at its start.
/// </summary>
public readonly record struct SoundWindow(Sound Sound, double? Loudness, double SpeechFrom = 0);

/// <summary>
/// The sound of the last windows of a stream, each about 5 seconds of audio. Songs sound like music almost throughout.
/// Ads, news and presenters mix speech with jingles and music beds, so a single window says little and the recent
/// ones are weighed together: some speech in the last half minute means talking, a stretch without any means music.
/// </summary>
public sealed class SoundHistory
{
    /// <summary>Below this score for both music and speech, a window is silence or noise.</summary>
    public const float MinScore = 0.15f;

    private const int Capacity = 6;
    private readonly Queue<Sound> _windows = new(Capacity);
    private readonly Queue<double> _speechFrom = new(Capacity);

    /// <summary>How many windows in a row were music, also beyond the ones kept.</summary>
    public int ConsecutiveMusic { get; private set; }

    /// <summary>Speech in 2 of the last 6 windows, music without speech in the last 4, or else unknown.</summary>
    public Sound Current
    {
        get
        {
            if (_windows.Count < 3)
            {
                return Sound.Unknown;
            }

            if (_windows.Count(w => w == Sound.Speech) >= 2)
            {
                return Sound.Speech;
            }

            var recent = _windows.Skip(Math.Max(0, _windows.Count - 4)).ToList();
            return !recent.Contains(Sound.Speech) && recent.Count(w => w == Sound.Music) >= 3 ? Sound.Music : Sound.Unknown;
        }
    }

    /// <summary>
    /// How long ago the latest stretch of talking began: counted back from the newest window with speech, through
    /// speech and the unclear windows between it (a jingle, a sound effect), up to the oldest speech window before a
    /// window of music, and within that window to where the talking starts. Zero when none of the kept windows is
    /// speech. Says where a break heard as speech really started, which is a window or two before it counts as one.
    /// Dating it to the start of its first window instead cuts off the last seconds of the song before it.
    /// </summary>
    public TimeSpan SpeechStretch
    {
        get
        {
            var windows = _windows.ToArray();
            var newest = Array.LastIndexOf(windows, Sound.Speech);
            if (newest < 0)
            {
                return TimeSpan.Zero;
            }

            var oldest = newest;
            for (var i = newest - 1; i >= 0 && windows[i] != Sound.Music; i--)
            {
                if (windows[i] == Sound.Speech)
                {
                    oldest = i;
                }
            }

            return (windows.Length - oldest - _speechFrom.ElementAt(oldest)) * SongClock.Window;
        }
    }

    /// <summary>Labels a window by its average YAMNet scores for the Speech and Music classes.</summary>
    public static Sound Label(float speech, float music) =>
        Math.Max(speech, music) < MinScore ? Sound.Unknown
        : speech > music ? Sound.Speech
        : Sound.Music;

    /// <summary>
    /// Where in a window of speech the talking begins, as a fraction of the window, from the YAMNet scores of its
    /// frames. The window is split where the frames before lean most to music and the frames after most to speech,
    /// which one stray frame either way does not move far.
    /// </summary>
    public static double SpeechFrom(IReadOnlyList<float> speech, IReadOnlyList<float> music)
    {
        if (speech.Count == 0)
        {
            return 0;
        }

        // Splitting at a frame moves the frames before it to the music side; the best split has the most speech
        // before it taken away, so it is where the running sum of speech over music is lowest.
        double sum = 0, lowest = 0;
        var split = 0;
        for (var frame = 0; frame < speech.Count; frame++)
        {
            sum += speech[frame] - music[frame];
            if (sum < lowest)
            {
                lowest = sum;
                split = frame + 1;
            }
        }

        return (double)split / speech.Count;
    }

    /// <param name="speechFrom">How far into a window of speech the talking begins, as a fraction of the window.</param>
    public void Add(Sound window, double speechFrom = 0)
    {
        if (_windows.Count == Capacity)
        {
            _windows.Dequeue();
            _speechFrom.Dequeue();
        }

        _windows.Enqueue(window);
        _speechFrom.Enqueue(window == Sound.Speech ? Math.Clamp(speechFrom, 0, 1) : 0);
        ConsecutiveMusic = window == Sound.Music ? ConsecutiveMusic + 1 : 0;
    }

    /// <summary>Forgets what was heard, for example when a new song starts.</summary>
    public void Clear()
    {
        _windows.Clear();
        _speechFrom.Clear();
        ConsecutiveMusic = 0;
    }
}
