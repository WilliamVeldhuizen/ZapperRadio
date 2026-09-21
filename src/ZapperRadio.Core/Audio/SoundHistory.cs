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
/// next station; ads and talk are mixed at a level of their own.
/// </summary>
public readonly record struct SoundWindow(Sound Sound, double? Loudness);

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
    /// How many windows ago the latest stretch of talking began: counted back from the newest window with speech,
    /// through speech and the unclear windows between it (a jingle, a sound effect), up to the oldest speech window
    /// before a window of music. Zero when none of the kept windows is speech. Says where a break heard as speech
    /// really started, which is a window or two before it counts as one.
    /// </summary>
    public int SpeechStretch
    {
        get
        {
            var windows = _windows.ToArray();
            var newest = Array.LastIndexOf(windows, Sound.Speech);
            if (newest < 0)
            {
                return 0;
            }

            var oldest = newest;
            for (var i = newest - 1; i >= 0 && windows[i] != Sound.Music; i--)
            {
                if (windows[i] == Sound.Speech)
                {
                    oldest = i;
                }
            }

            return windows.Length - oldest;
        }
    }

    /// <summary>Labels a window by its average YAMNet scores for the Speech and Music classes.</summary>
    public static Sound Label(float speech, float music) =>
        Math.Max(speech, music) < MinScore ? Sound.Unknown
        : speech > music ? Sound.Speech
        : Sound.Music;

    public void Add(Sound window)
    {
        if (_windows.Count == Capacity)
        {
            _windows.Dequeue();
        }

        _windows.Enqueue(window);
        ConsecutiveMusic = window == Sound.Music ? ConsecutiveMusic + 1 : 0;
    }

    /// <summary>Forgets what was heard, for example when a new song starts.</summary>
    public void Clear()
    {
        _windows.Clear();
        ConsecutiveMusic = 0;
    }
}
