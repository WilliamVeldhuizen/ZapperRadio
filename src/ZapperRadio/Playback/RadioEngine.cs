using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;
using ZapperRadio.Core.Models;
using ZapperRadio.Core.Playback;
using ZapperRadio.Core.Streaming;

namespace ZapperRadio.Playback;

/// <summary>
/// Keeps every favorite streaming (muted) in the background and unmutes the one being listened to.
/// A station that is not a favorite gets a temporary stream that is closed when you switch away,
/// unless it is added to the favorites while playing, in which case its stream is kept.
/// A station can also be played from a moment in the past, out of its time-shift buffer, so it starts at the
/// beginning of the song it plays rather than halfway into it. One more player does that for whichever station is
/// being listened to; the stream's own player stays muted meanwhile and keeps the station's titles and sound coming.
/// A zap can fade from one station into the next. The station faded out is always one played from its buffer,
/// because only there is the break known before it is heard; a spare replay player plays it out meanwhile, so the
/// replay of the next station can start alongside it.
/// </summary>
public sealed class RadioEngine : IDisposable
{
    /// <summary>A moment closer to the broadcast than this is not worth leaving the live stream for.</summary>
    private static readonly TimeSpan MinTimeShift = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long a zap takes to fade from one station into the next. The zapper acts a little ahead of what is heard,
    /// and the fade out has to fit in that lead, so it is over before the break it leaves begins.
    /// </summary>
    public static readonly TimeSpan CrossfadeLength = TimeSpan.FromMilliseconds(400);

    private readonly StreamUrlResolver _resolver;
    private readonly IcyProxy? _proxy;
    private readonly TrackDurations? _durations;
    private readonly SoundClassifier? _classifier;
    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<string, StationStream> _favorites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _knownLoudness = new(StringComparer.Ordinal);
    private readonly DispatcherQueueTimer _delayTimer;
    private StationStream? _transient;
    private double _volume = 0.8;
    private bool _isMuted;
    private bool _normalizeLoudness = true;
    private TimeSpan _timeShift = TimeSpan.FromMinutes(5);

    private MediaPlayer? _replayPlayer;
    private MediaSource? _replaySource;
    private Uri? _replayUrl;

    /// <summary>When the audio the replay started with came in from the station.</summary>
    private DateTimeOffset _replayFrom;

    /// <summary>How long the replay played before <see cref="_replayPlayingSince"/>: the time it spent opening or buffering does not count.</summary>
    private TimeSpan _replayPlayed;

    private DateTimeOffset? _replayPlayingSince;
    private int _shownDelaySeconds;

    /// <summary>How far the replay is faded in; below 1 only while a zap fades into it.</summary>
    private double _replayFade = 1;

    private readonly DispatcherQueueTimer _fadeTimer;

    /// <summary>The replay of the station zapped away from, playing out while the next one fades in.</summary>
    private MediaPlayer? _fadeOutPlayer;
    private MediaSource? _fadeOutSource;
    private Uri? _fadeOutUrl;
    private StationStream? _fadeOutStream;
    private DateTimeOffset _fadeOutStart;

    /// <summary>A replay player with nothing to play, kept for the next fade so it need not be made again.</summary>
    private MediaPlayer? _spareReplayPlayer;

    /// <summary>The station whose own player fades in, when a zap lands on it live.</summary>
    private StationStream? _fadeInStream;

    /// <summary>Whether the replay fades in, which starts once it actually plays.</summary>
    private bool _fadeInReplay;

    private DateTimeOffset? _fadeInStart;

    public RadioEngine(StreamUrlResolver resolver, IcyProxy? proxy, TrackDurations? durations, SoundClassifier? classifier, DispatcherQueue dispatcher)
    {
        _resolver = resolver;
        _proxy = proxy;
        _durations = durations;
        _classifier = classifier;
        _dispatcher = dispatcher;

        // The delay only moves while the replay opens or buffers, but that is exactly when it is worth showing.
        _delayTimer = dispatcher.CreateTimer();
        _delayTimer.Interval = TimeSpan.FromSeconds(1);
        _delayTimer.Tick += (_, _) => RaiseDelayChanged();

        _fadeTimer = dispatcher.CreateTimer();
        _fadeTimer.Interval = TimeSpan.FromMilliseconds(15);
        _fadeTimer.Tick += (_, _) => OnFadeTick();
    }

    public StationStream? Active { get; private set; }

    public event EventHandler? ActiveChanged;

    public event EventHandler<StationStream>? StreamStatusChanged;

    public event EventHandler<StationStream>? StreamMetadataChanged;

    public event EventHandler<StationStream>? StreamLoudnessChanged;

    /// <summary>Raised when <see cref="Delay"/> moves by a second or more, and when it starts or ends.</summary>
    public event EventHandler? DelayChanged;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            foreach (var stream in AllStreams())
            {
                stream.Volume = value;
            }
        }
    }

    /// <summary>Whether every station is brought to the same loudness, so switching does not change the volume.</summary>
    public bool NormalizeLoudness
    {
        get => _normalizeLoudness;
        set
        {
            _normalizeLoudness = value;
            foreach (var stream in AllStreams())
            {
                stream.NormalizeLoudness = value;
            }
        }
    }

    /// <summary>
    /// How much of every station is kept to play back from; zero plays every station live. Changing it throws away
    /// what was kept, so the station being listened to goes live first.
    /// </summary>
    public TimeSpan TimeShift
    {
        get => _timeShift;
        set
        {
            if (value == _timeShift)
            {
                return;
            }

            _timeShift = value;
            FinishFade();
            GoLive();
            foreach (var stream in AllStreams())
            {
                stream.Buffer.Resize(value);
            }
        }
    }

    /// <summary>
    /// The memory the time-shift buffers of the favorites take at <paramref name="length"/>, from the bitrate each
    /// station announced. A station that does not say is counted at the bitrate its buffer is made for.
    /// </summary>
    public long BufferBytes(TimeSpan length) =>
        _favorites.Values.Sum(s => (long)TimeShiftBuffer.CapacityFor(length, s.Buffer.BitrateKbps));

    /// <summary>Whether the station being listened to is played from its buffer, behind the broadcast.</summary>
    public bool IsTimeShifted => _replayUrl is not null;

    /// <summary>How far behind the broadcast the station being listened to is heard; zero while it plays live.</summary>
    public TimeSpan Delay
    {
        get
        {
            if (!IsTimeShifted)
            {
                return TimeSpan.Zero;
            }

            var now = DateTimeOffset.UtcNow;
            var played = _replayPlayed + (_replayPlayingSince is { } since ? now - since : TimeSpan.Zero);
            var delay = now - (_replayFrom + played);
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }
    }

    /// <summary>The moment of the broadcast that is being heard.</summary>
    public DateTimeOffset HeardAt => DateTimeOffset.UtcNow - Delay;

    /// <summary>
    /// What <paramref name="stream"/> does in what is heard of it: the moment being played back for the station being
    /// listened to, and the live one for every other station.
    /// </summary>
    public StreamMoment HeardOf(StationStream stream) => stream == Active && IsTimeShifted ? stream.MomentAt(HeardAt) : stream.Moment;

    /// <summary>Has every running station measure its loudness again, for when the correction of one sounds off.</summary>
    public void RemeasureLoudness()
    {
        foreach (var stream in AllStreams())
        {
            stream.Remeasure();
        }
    }

    /// <summary>
    /// Hands a station the loudness measured for it in an earlier run. It applies to the streams opened from here on,
    /// because a stream that is already running is measuring for itself.
    /// </summary>
    public void SetKnownLoudness(string url, double loudness) => _knownLoudness[url] = loudness;

    /// <summary>Silences the station being listened to, also after switching, without stopping it.</summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (_fadeOutPlayer is not null)
            {
                _fadeOutPlayer.IsMuted = value;
            }

            if (IsTimeShifted)
            {
                _replayPlayer!.IsMuted = value;
            }
            else if (Active is not null)
            {
                Active.IsMuted = value;
            }
        }
    }

    public StationStream? Find(string url) =>
        _favorites.GetValueOrDefault(url) ?? (_transient?.Station.Url == url ? _transient : null);

    /// <summary>Starts streams for new favorites and stops streams for removed ones.</summary>
    public void SetFavorites(IEnumerable<Station> favorites)
    {
        // A station that fades out may be one that is no longer wanted, so it is played out first.
        FinishFade();
        var wanted = favorites.DistinctBy(s => s.Url).ToDictionary(s => s.Url, StringComparer.Ordinal);

        foreach (var (url, stream) in _favorites.Where(f => !wanted.ContainsKey(f.Key)).ToList())
        {
            _favorites.Remove(url);
            if (stream == Active)
            {
                // Keep listening; it just won't stay warm after switching away.
                if (_transient is not null)
                {
                    Release(_transient);
                }

                _transient = stream;
            }
            else
            {
                Release(stream);
            }
        }

        foreach (var (url, station) in wanted.Where(w => !_favorites.ContainsKey(w.Key)))
        {
            if (_transient?.Station.Url == url)
            {
                _favorites[url] = _transient;
                _transient = null;
            }
            else
            {
                _favorites[url] = CreateAndStart(station);
            }
        }
    }

    /// <summary>
    /// Listens to a station. With <paramref name="from"/> it is played from that moment of its broadcast, when its
    /// buffer still holds it, and live otherwise. Picking the station that is already on changes nothing.
    /// With <paramref name="crossfade"/> it fades in over <see cref="CrossfadeLength"/>, and a station played from its
    /// buffer before it fades out meanwhile; one played live is cut off, because its break is already being heard.
    /// </summary>
    public void Play(Station station, DateTimeOffset? from = null, bool crossfade = false)
    {
        var stream = Find(station.Url) ?? CreateAndStart(station);
        var previous = Active;
        if (previous == stream)
        {
            return;
        }

        FinishFade();
        if (previous is not null)
        {
            previous.IsMuted = true;
        }

        if (crossfade && IsTimeShifted)
        {
            FadeOutReplay(previous!);
        }
        else
        {
            StopReplay();
        }

        if (_transient is not null && _transient != stream)
        {
            // A station that fades out still plays from its buffer, so it is released once it has faded out.
            if (_transient != _fadeOutStream)
            {
                Release(_transient);
            }

            _transient = null;
        }

        if (!_favorites.ContainsKey(stream.Station.Url))
        {
            _transient = stream;
        }

        Active = stream;
        if (FindPosition(stream, from) is { } position)
        {
            StartReplay(stream, position, crossfade);
        }
        else if (crossfade)
        {
            stream.Fade = 0;
            stream.IsMuted = _isMuted;
            _fadeInStream = stream;
            _fadeInStart = DateTimeOffset.UtcNow;
            _fadeTimer.Start();
        }
        else
        {
            stream.IsMuted = _isMuted;
        }

        ActiveChanged?.Invoke(this, EventArgs.Empty);
        RaiseDelayChanged(force: true);
    }

    /// <summary>Plays the station being listened to live again, for when what is on right now is what you want to hear.</summary>
    public void GoLive()
    {
        if (!IsTimeShifted || Active is not { } active)
        {
            return;
        }

        StopReplay();
        active.IsMuted = _isMuted;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
        RaiseDelayChanged(force: true);
    }

    public void Stop()
    {
        if (Active is null)
        {
            return;
        }

        FinishFade();
        Active.IsMuted = true;
        StopReplay();
        if (Active == _transient)
        {
            Release(_transient);
            _transient = null;
        }

        Active = null;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
        RaiseDelayChanged(force: true);
    }

    /// <summary>Where in its buffer <paramref name="stream"/> has the moment <paramref name="from"/>, or null to play it live.</summary>
    private long? FindPosition(StationStream stream, DateTimeOffset? from) =>
        _proxy is not null && from is { } moment && DateTimeOffset.UtcNow - moment >= MinTimeShift
            ? stream.Buffer.PositionAt(moment)
            : null;

    /// <param name="fadeIn">Whether it fades in, from the moment it starts to play.</param>
    private void StartReplay(StationStream stream, long position, bool fadeIn = false)
    {
        _replayPlayer ??= TakeSpareReplayPlayer();
        _replayUrl = _proxy!.RegisterReplay(stream.Buffer, position, new Uri(stream.Station.Url));
        _replayFrom = stream.Buffer.TimeAt(position) ?? DateTimeOffset.UtcNow;
        _replayPlayed = TimeSpan.Zero;
        _replayPlayingSince = null;
        _replayFade = fadeIn ? 0 : 1;
        _fadeInReplay = fadeIn;

        _replaySource = MediaSource.CreateFromUri(_replayUrl);
        _replayPlayer.Volume = stream.OutputVolume * _replayFade;
        _replayPlayer.IsMuted = _isMuted;
        _replayPlayer.Source = _replaySource;
        _delayTimer.Start();
    }

    private void StopReplay()
    {
        if (_replayUrl is null)
        {
            return;
        }

        _delayTimer.Stop();
        _replayPlayer!.Source = null;
        _replaySource?.Dispose();
        _replaySource = null;
        _proxy!.Unregister(_replayUrl);
        _replayUrl = null;
        _replayPlayingSince = null;

        // A replay that is gone has nothing left to fade in.
        _replayFade = 1;
        _fadeInReplay = false;
        if (_fadeInStream is null)
        {
            _fadeInStart = null;
        }
    }

    /// <summary>
    /// Hands the replay of <paramref name="stream"/> over to fade out, which frees the replay player for the station
    /// zapped to. The time it plays out is not counted as delay: it is no longer the station being listened to.
    /// </summary>
    private void FadeOutReplay(StationStream stream)
    {
        _delayTimer.Stop();
        _fadeOutPlayer = _replayPlayer;
        _fadeOutSource = _replaySource;
        _fadeOutUrl = _replayUrl;
        _fadeOutStream = stream;
        _fadeOutStart = DateTimeOffset.UtcNow;

        _replayPlayer = null;
        _replaySource = null;
        _replayUrl = null;
        _replayPlayingSince = null;
        _fadeTimer.Start();
    }

    private void OnFadeTick()
    {
        var now = DateTimeOffset.UtcNow;
        if (_fadeOutPlayer is not null)
        {
            var progress = FadeProgress(_fadeOutStart, now);
            if (progress >= 1)
            {
                EndFadeOut();
            }
            else
            {
                // An equal-power curve, so the two stations together do not dip in loudness halfway.
                _fadeOutPlayer.Volume = _fadeOutStream!.OutputVolume * Math.Cos(progress * Math.PI / 2);
            }
        }

        if (_fadeInStart is { } start)
        {
            var progress = FadeProgress(start, now);
            if (progress >= 1)
            {
                EndFadeIn();
            }
            else
            {
                SetFadeIn(Math.Sin(progress * Math.PI / 2));
            }
        }

        // A replay that fades in is still opening; it starts the timer again once it plays.
        if (_fadeOutPlayer is null && _fadeInStart is null)
        {
            _fadeTimer.Stop();
        }
    }

    private static double FadeProgress(DateTimeOffset start, DateTimeOffset now) =>
        Math.Clamp((now - start) / CrossfadeLength, 0, 1);

    private void SetFadeIn(double level)
    {
        if (_fadeInStream is not null)
        {
            _fadeInStream.Fade = level;
        }
        else if (_fadeInReplay && IsTimeShifted && Active is not null)
        {
            _replayFade = level;
            _replayPlayer!.Volume = Active.OutputVolume * level;
        }
    }

    private void EndFadeIn()
    {
        SetFadeIn(1);
        _fadeInStream = null;
        _fadeInReplay = false;
        _fadeInStart = null;
    }

    private void EndFadeOut()
    {
        if (_fadeOutPlayer is null)
        {
            return;
        }

        _fadeOutPlayer.Source = null;
        _fadeOutSource?.Dispose();
        _proxy!.Unregister(_fadeOutUrl!);
        if (_spareReplayPlayer is null)
        {
            _spareReplayPlayer = _fadeOutPlayer;
        }
        else
        {
            _fadeOutPlayer.Dispose();
        }

        // A station that was not a favorite had its stream kept only to play it out.
        var stream = _fadeOutStream!;
        if (stream != Active && stream != _transient && !_favorites.ContainsValue(stream))
        {
            Release(stream);
        }

        _fadeOutPlayer = null;
        _fadeOutSource = null;
        _fadeOutUrl = null;
        _fadeOutStream = null;
    }

    /// <summary>Ends a fade that is still going on at once, for when something else is about to change what is heard.</summary>
    private void FinishFade()
    {
        EndFadeOut();
        if (_fadeInStream is not null || _fadeInReplay)
        {
            EndFadeIn();
        }

        _fadeTimer.Stop();
    }

    private MediaPlayer TakeSpareReplayPlayer()
    {
        var player = _spareReplayPlayer ?? CreateReplayPlayer();
        _spareReplayPlayer = null;
        return player;
    }

    private MediaPlayer CreateReplayPlayer()
    {
        var player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Media, AutoPlay = true };
        // The app has its own media card; this player is only ever a stand-in for a station's own.
        player.CommandManager.IsEnabled = false;
        player.PlaybackSession.PlaybackStateChanged += (session, _) =>
        {
            var state = session.PlaybackState;
            // Two replay players take turns, and only the one playing the station being listened to counts.
            _dispatcher.TryEnqueue(() =>
            {
                if (player == _replayPlayer)
                {
                    OnReplayStateChanged(state);
                }
            });
        };
        player.MediaFailed += (_, _) =>
        {
            var url = _replayUrl;
            // The live stream is still there, so a replay that cannot be played is no reason to go silent.
            _dispatcher.TryEnqueue(() =>
            {
                if (url is not null && url == _replayUrl)
                {
                    GoLive();
                }
            });
        };
        return player;
    }

    /// <summary>Counts only the time the replay actually plays, so the delay grows while it opens or buffers.</summary>
    private void OnReplayStateChanged(MediaPlaybackState state)
    {
        if (!IsTimeShifted)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (state == MediaPlaybackState.Playing)
        {
            _replayPlayingSince ??= now;
            if (_fadeInReplay && _fadeInStart is null)
            {
                _fadeInStart = now;
                _fadeTimer.Start();
            }
        }
        else if (_replayPlayingSince is { } since)
        {
            _replayPlayed += now - since;
            _replayPlayingSince = null;
        }

        RaiseDelayChanged(force: true);
    }

    private void RaiseDelayChanged(bool force = false)
    {
        var seconds = (int)Delay.TotalSeconds;
        if (force || seconds != _shownDelaySeconds)
        {
            _shownDelaySeconds = seconds;
            DelayChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private StationStream CreateAndStart(Station station)
    {
        var stream = new StationStream(station, _resolver, _proxy, _durations, _classifier, _dispatcher, _volume, _timeShift)
        {
            NormalizeLoudness = _normalizeLoudness,
        };
        if (_knownLoudness.TryGetValue(station.Url, out var loudness))
        {
            stream.SeedLoudness(loudness);
        }

        stream.StatusChanged += OnStreamStatusChanged;
        stream.MetadataChanged += OnStreamMetadataChanged;
        stream.LoudnessChanged += OnStreamLoudnessChanged;
        stream.OutputVolumeChanged += OnStreamOutputVolumeChanged;
        stream.Start();
        return stream;
    }

    private void Release(StationStream stream)
    {
        stream.StatusChanged -= OnStreamStatusChanged;
        stream.MetadataChanged -= OnStreamMetadataChanged;
        stream.LoudnessChanged -= OnStreamLoudnessChanged;
        stream.OutputVolumeChanged -= OnStreamOutputVolumeChanged;
        stream.Dispose();
    }

    private void OnStreamStatusChanged(object? sender, EventArgs e) =>
        StreamStatusChanged?.Invoke(this, (StationStream)sender!);

    private void OnStreamMetadataChanged(object? sender, EventArgs e) =>
        StreamMetadataChanged?.Invoke(this, (StationStream)sender!);

    private void OnStreamLoudnessChanged(object? sender, EventArgs e)
    {
        var stream = (StationStream)sender!;
        if (stream.MeasuredLoudness is { } loudness)
        {
            // So a station that is opened again later, after being dropped as a favorite, starts where it left off.
            _knownLoudness[stream.Station.Url] = loudness;
        }

        StreamLoudnessChanged?.Invoke(this, stream);
    }

    /// <summary>The replay stands in for the station's own player, so it follows that player's volume and gain.</summary>
    private void OnStreamOutputVolumeChanged(object? sender, EventArgs e)
    {
        if (IsTimeShifted && sender == Active)
        {
            _replayPlayer!.Volume = Active!.OutputVolume * _replayFade;
        }
    }

    private IEnumerable<StationStream> AllStreams() =>
        _transient is null ? _favorites.Values : _favorites.Values.Append(_transient);

    public void Dispose()
    {
        FinishFade();
        StopReplay();
        _replayPlayer?.Dispose();
        _spareReplayPlayer?.Dispose();
        foreach (var stream in AllStreams().ToList())
        {
            Release(stream);
        }

        _favorites.Clear();
        _transient = null;
        Active = null;
    }
}
