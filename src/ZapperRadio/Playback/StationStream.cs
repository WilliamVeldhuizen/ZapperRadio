using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;
using ZapperRadio.Core.Audio;
using ZapperRadio.Core.Models;
using ZapperRadio.Core.Playback;
using ZapperRadio.Core.Streaming;

namespace ZapperRadio.Playback;

public enum StreamStatus
{
    Connecting,
    Live,
    Buffering,
    Reconnecting,
    Failed,
}

/// <summary>
/// A single station stream that keeps running for as long as it exists. It starts muted;
/// listening to it is a matter of unmuting, so switching never opens a new connection
/// (which is where most stations insert their pre-roll ads).
/// It also keeps a running loudness estimate of the station, so the loud ones are turned down to the level of
/// the rest and zapping does not change how loud the music is.
/// Dropped connections are re-established automatically with a back-off.
/// All members must be used on the UI thread; player events are marshalled to it.
/// </summary>
public sealed class StationStream : IDisposable
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    /// <summary>How long a stream may be opening or buffering before it is considered stuck.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(45);

    /// <summary>How long after the last classified window the stream still counts as listened to.</summary>
    private static readonly TimeSpan ListeningTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How far a new loudness estimate has to be from the one in use before the volume is moved, in decibels.
    /// Every window of music refines the estimate a little, and following it exactly would keep nudging the
    /// volume while a song plays.
    /// </summary>
    private const double MinLoudnessChange = 0.5;

    private readonly DispatcherQueue _dispatcher;
    private readonly StreamUrlResolver _resolver;
    private readonly IcyProxy? _proxy;
    private readonly TrackDurations? _durations;
    private readonly SoundClassifier? _classifier;
    private readonly SoundHistory _sound = new();
    private StationLoudness _loudness = new();
    private readonly SongClock _songClock = new();
    private readonly UnmarkedAdBreak _unmarkedAdBreak = new();
    private readonly StreamTimeline _timeline = new();
    private readonly MediaPlayer _player;
    private readonly DispatcherQueueTimer _watchdog;
    private readonly DispatcherQueueTimer _retryTimer;
    private readonly DispatcherQueueTimer _songEndTimer;
    private CancellationTokenSource? _connectCts;
    private MediaSource? _source;
    private Uri? _relayUrl;
    private SoundClassifier.Listener? _listener;
    private DateTime _lastSoundUtc;
    private int _failedAttempts;
    private DateTime _lastPlayingUtc;
    private int _songNumber;
    private double _volume;
    private double _fade = 1;
    private bool _normalizeLoudness = true;
    private double? _measuredLoudness;
    private bool _isRemeasuring;
    private bool _disposed;

    /// <param name="proxy">Relays the stream to read its song titles; null plays the station directly.</param>
    /// <param name="durations">Looks up song lengths to recognize unmarked ad breaks; null relies on ad markers only.</param>
    /// <param name="classifier">Hears whether the relayed stream plays music or speech; null does not listen.</param>
    /// <param name="timeShift">How much of the relayed stream is kept to play back from; zero keeps nothing.</param>
    public StationStream(Station station, StreamUrlResolver resolver, IcyProxy? proxy, TrackDurations? durations, SoundClassifier? classifier, DispatcherQueue dispatcher, double volume, TimeSpan timeShift)
    {
        Station = station;
        Buffer = new TimeShiftBuffer(timeShift);
        _resolver = resolver;
        _proxy = proxy;
        _durations = durations;
        _classifier = classifier;
        _dispatcher = dispatcher;
        _volume = volume;

        _player = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media,
            AutoPlay = true,
            IsMuted = true,
            Volume = volume,
        };
        // With several players alive, the system media overlay would only get confused.
        _player.CommandManager.IsEnabled = false;
        _player.MediaOpened += OnMediaOpened;
        _player.MediaFailed += OnMediaFailed;
        _player.MediaEnded += OnMediaEnded;
        _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;

        _watchdog = dispatcher.CreateTimer();
        _watchdog.Interval = TimeSpan.FromSeconds(5);
        _watchdog.Tick += (_, _) =>
        {
            CheckForStall();
            // A stream that cannot be heard gets no windows to anchor its song clock to, so it is nudged here.
            UpdateSongClock();
            // Nothing older than the buffer can be played back, so nothing older is worth remembering.
            _timeline.Prune(DateTimeOffset.UtcNow - Buffer.Length - TimeSpan.FromMinutes(1));
        };

        _retryTimer = dispatcher.CreateTimer();
        _retryTimer.IsRepeating = false;
        _retryTimer.Tick += (_, _) => _ = ConnectAsync();

        _songEndTimer = dispatcher.CreateTimer();
        _songEndTimer.IsRepeating = false;
        _songEndTimer.Tick += (_, _) => MarkSongOverdue();
    }

    public Station Station { get; }

    /// <summary>The last minutes of the station, kept while it is relayed, to play back from.</summary>
    public TimeShiftBuffer Buffer { get; }

    /// <summary>What the stream is doing now, as recorded in its timeline.</summary>
    public StreamMoment Moment => _timeline.Latest ?? StreamMoment.Unknown;

    /// <summary>What the stream was doing at <paramref name="time"/>, for playing it back from the buffer.</summary>
    public StreamMoment MomentAt(DateTimeOffset time) => _timeline.At(time) ?? StreamMoment.Unknown;

    /// <summary>When the stream did something else after <paramref name="time"/>, or null when it has not since.</summary>
    public DateTimeOffset? NextMomentAfter(DateTimeOffset time) => _timeline.NextChangeAfter(time);

    /// <summary>
    /// When the song the station plays now began, or null when it plays none, or it cannot be told. The song clock
    /// knows best; a station without titles is timed from the start of the music heard, counted back from the last
    /// window rather than from now, so it does not drift in the seconds between two windows.
    /// </summary>
    public DateTimeOffset? SongStartedAt =>
        IsInAdBreak ? null
        : _songClock.BeganAt is { } began ? began
        : Sound == Sound.Music ? new DateTimeOffset(_lastSoundUtc, TimeSpan.Zero) - _sound.ConsecutiveMusic * SongClock.Window
        : null;

    public StreamStatus Status { get; private set; } = StreamStatus.Connecting;

    public string? LastError { get; private set; }

    public event EventHandler? StatusChanged;

    /// <summary>The latest song title (or ad marker) the station sent, or null when there is none.</summary>
    public IcyMetadata? Metadata { get; private set; }

    /// <summary>
    /// True when the current song should have ended a while ago and no new title came, which usually
    /// means the station is playing ads without marking them. It is timed from the moment the audio says
    /// the song started (<see cref="SongClock"/>), not from the moment its title arrived.
    /// </summary>
    public bool IsSongOverdue { get; private set; }

    /// <summary>
    /// True when the song is overdue and, if the stream is being listened to, speech is heard: an ad break the
    /// station does not mark. Music without a new title is just a longer song or one the station sent no title for.
    /// </summary>
    public bool IsAssumedAdBreak => _unmarkedAdBreak.IsActive;

    /// <summary>True during an ad break the station marks, or an assumed one.</summary>
    public bool IsInAdBreak => Metadata?.IsAd == true || IsAssumedAdBreak;

    /// <summary>Whether the sound classifier heard this stream recently.</summary>
    public bool IsListening => _listener is not null && DateTime.UtcNow - _lastSoundUtc < ListeningTimeout;

    /// <summary>
    /// Whether the stream can be heard at all: it is relayed through the classifier and either has been heard
    /// recently or has not had the time to be heard yet. Unlike <see cref="IsListening"/> this stays true in the
    /// seconds before the first window comes in, so the song clock waits for the audio instead of giving up on it.
    /// </summary>
    private bool CanHear => _listener is not null && (_lastSoundUtc == default || IsListening);

    /// <summary>What the stream sounds like: music or speech. Unknown when it is not listened to or the sound is mixed.</summary>
    public Sound Sound => IsListening ? _sound.Current : Sound.Unknown;

    /// <summary>Raised when <see cref="Metadata"/>, <see cref="IsInAdBreak"/> or <see cref="Sound"/> changes.</summary>
    public event EventHandler? MetadataChanged;

    public bool IsMuted
    {
        get => _player.IsMuted;
        set => _player.IsMuted = value;
    }

    /// <summary>The volume the player is set to, before the loudness of this station is corrected for.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            ApplyVolume();
        }
    }

    /// <summary>Whether the measured loudness is corrected for.</summary>
    public bool NormalizeLoudness
    {
        get => _normalizeLoudness;
        set
        {
            _normalizeLoudness = value;
            ApplyVolume();
        }
    }

    /// <summary>
    /// How loud the music of this station is in LUFS, or null while too little of it has been heard. Streams that
    /// are not listened to (HLS, which skips the relay) never get one and are left at the volume they come in at.
    /// </summary>
    public double? MeasuredLoudness => _measuredLoudness;

    /// <summary>The correction the measured loudness asks for, in decibels, or 0 while there is none to apply.</summary>
    public double MeasuredGainDb =>
        _normalizeLoudness && _measuredLoudness is { } loudness
            ? Math.Clamp(StationLoudness.Target - loudness, StationLoudness.MinGainDb, StationLoudness.MaxGainDb)
            : 0;

    /// <summary>Everything that is done to the volume of this station.</summary>
    public double GainDb => MeasuredGainDb;

    /// <summary>True from <see cref="Remeasure"/> until the new estimate is in; the correction of the old one stays in use meanwhile.</summary>
    public bool IsRemeasuring => _isRemeasuring;

    /// <summary>Raised when <see cref="MeasuredLoudness"/> changes, which is at most once per window of music.</summary>
    public event EventHandler? LoudnessChanged;

    /// <summary>
    /// Starts from the loudness measured for this station in an earlier run, so its correction applies right away
    /// instead of after the minute of music it takes to measure again. Only meaningful before the stream starts.
    /// </summary>
    public void SeedLoudness(double loudness)
    {
        _measuredLoudness = loudness;
        ApplyVolume();
    }

    /// <summary>
    /// Throws away what was heard of this station so far and measures it again, for when the estimate turned out
    /// wrong. The volume keeps the old correction until about a minute of music has been heard again, and then takes
    /// over the new estimate right away, however close it is to the old one.
    /// </summary>
    public void Remeasure()
    {
        _loudness = new StationLoudness();
        _isRemeasuring = _measuredLoudness is not null;
        LoudnessChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Start() => _ = ConnectAsync();

    private async Task ConnectAsync()
    {
        if (_disposed)
        {
            return;
        }

        _retryTimer.Stop();
        _connectCts?.Cancel();
        var cts = _connectCts = new CancellationTokenSource();
        SetStatus(_failedAttempts == 0 ? StreamStatus.Connecting : StreamStatus.Reconnecting);

        try
        {
            var uri = await _resolver.ResolveAsync(new Uri(Station.Url), cts.Token);
            if (cts.IsCancellationRequested || _disposed)
            {
                return;
            }

            ReleaseSource();
            _source = MediaSource.CreateFromUri(RelayForTitles(uri));
            _player.Source = _source;
            _lastPlayingUtc = DateTime.UtcNow;
            _watchdog.Start();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ScheduleReconnect(ex.Message);
        }
    }

    private Uri RelayForTitles(Uri uri)
    {
        // HLS playlists reference their segments relatively, so they cannot go through the relay.
        if (_proxy is null || StreamUrlResolver.GetPlaylistKind(uri) != PlaylistKind.None
                           || uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        Uri? relayUrl = null;
        SoundClassifier.Listener? listener = null;
        listener = _listener = _classifier?.Listen(window => OnUiThread(() =>
        {
            if (_listener == listener)
            {
                AddSound(window);
            }
        }));
        relayUrl = _relayUrl = _proxy.Register(uri, metadata => OnUiThread(() =>
        {
            // Ignore a relay that is being replaced by a new connection.
            if (_relayUrl == relayUrl)
            {
                // Between a song and the ads, some stations clear the title and others put their own name or the
                // name of the program there. Neither is the next song, so the clock of the one that was playing
                // keeps running and the ads that follow are still noticed when it runs over.
                SetMetadata(
                    metadata is { Title: null, IsAd: false } ? null : metadata,
                    keepSongEnd: metadata is { IsAd: false, IsSong: false });
            }
        }), listener is null ? null : listener.Write, Buffer);
        return relayUrl;
    }

    private void SetMetadata(IcyMetadata? metadata, bool keepSongEnd = false)
    {
        if (Metadata == metadata)
        {
            return;
        }

        Metadata = metadata;
        if (!keepSongEnd)
        {
            _songNumber++;
            _songEndTimer.Stop();
            _songClock.Stop();
            IsSongOverdue = false;
            // What the previous song or ad sounded like says nothing about the new one.
            _sound.Clear();
        }

        // Before the change is recorded, so the timeline knows where the new song begins.
        if (_durations is not null && metadata is { IsSong: true })
        {
            _songClock.Start(DateTimeOffset.UtcNow);
        }

        UpdateAssumedAdBreak();
        OnMetadataChanged();

        if (_durations is not null && metadata is { IsSong: true, Title: { } title })
        {
            _ = WatchSongEndAsync(title);
        }
    }

    /// <summary>Looks up how long the song lasts, so <see cref="SongClock"/> knows when it should have ended.</summary>
    private async Task WatchSongEndAsync(string title)
    {
        var songNumber = _songNumber;
        var duration = await _durations!.GetAsync(title);
        OnUiThread(() =>
        {
            if (duration is not null && songNumber == _songNumber)
            {
                _songClock.SetLength(duration.Value);
                UpdateSongClock();
            }
        });
    }

    /// <summary>
    /// Waits for the end of the song from the moment the audio says it started, rather than from the moment its
    /// title arrived, which on many stations is a good deal earlier. The wait is set again whenever the clock
    /// moves, which it does at most once per song.
    /// </summary>
    private void UpdateSongClock()
    {
        _songClock.Update(CanHear ? _sound : null, DateTimeOffset.UtcNow);
        if (IsSongOverdue || _songClock.OverdueAt is not { } overdue)
        {
            return;
        }

        var remaining = overdue - DateTimeOffset.UtcNow;
        _songEndTimer.Stop();
        _songEndTimer.Interval = remaining > TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
        _songEndTimer.Start();
    }

    private void MarkSongOverdue()
    {
        if (!IsSongOverdue)
        {
            IsSongOverdue = true;
            UpdateAssumedAdBreak();
            OnMetadataChanged();
        }
    }

    private void AddSound(SoundWindow window)
    {
        var before = Sound;
        _lastSoundUtc = DateTime.UtcNow;
        _sound.Add(window.Sound, window.SpeechFrom);
        if (window.Loudness is { } loudness)
        {
            _loudness.Add(loudness);
            UpdateLoudness();
        }

        UpdateSongClock();
        if (UpdateAssumedAdBreak() | Sound != before)
        {
            OnMetadataChanged();
        }
    }

    /// <summary>Takes over a new estimate once it is far enough from the one in use to be worth moving the volume.</summary>
    private void UpdateLoudness()
    {
        if (_loudness.Value is not { } loudness
            || (!_isRemeasuring && _measuredLoudness is { } current && Math.Abs(current - loudness) < MinLoudnessChange))
        {
            return;
        }

        _isRemeasuring = false;
        _measuredLoudness = loudness;
        ApplyVolume();
        LoudnessChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// How far the station's own player is faded in, from 0 to 1, while a zap fades from one station into another.
    /// Only its own player: one that plays the station back from the buffer is faded apart from it.
    /// </summary>
    public double Fade
    {
        get => _fade;
        set
        {
            _fade = value;
            _player.Volume = OutputVolume * value;
        }
    }

    /// <summary>Sets the player to the volume with the gain of this station applied; a boost stops at full volume.</summary>
    private void ApplyVolume()
    {
        _player.Volume = OutputVolume * _fade;
        OutputVolumeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The volume the station is heard at, with its gain applied, for a player that plays it back from the buffer.</summary>
    public double OutputVolume => Math.Clamp(_volume * Loudness.Linear(GainDb), 0, 1);

    /// <summary>Raised when <see cref="OutputVolume"/> may have changed.</summary>
    public event EventHandler? OutputVolumeChanged;

    /// <summary>
    /// Records what the stream does now in its timeline, dating a break the sound gave away back to where the talking
    /// began and a new song back to where it began, and tells the listeners.
    /// </summary>
    private void OnMetadataChanged()
    {
        var sound = Sound;
        var moment = new StreamMoment(Metadata, IsAssumedAdBreak, sound, Channel.StateOf(IsInAdBreak, isLive: true, Metadata?.IsSong == true, sound));
        // Played back from the buffer, a stream is heard whether or not its live connection is up, so the timeline
        // leaves the connection out and only says what the audio was.
        var now = DateTimeOffset.UtcNow;
        _timeline.Record(_timeline.StartOf(moment, _sound, SongStartedAt, now), moment);
        MetadataChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool UpdateAssumedAdBreak() => _unmarkedAdBreak.Update(IsSongOverdue, IsListening, _sound);

    private void ScheduleReconnect(string reason)
    {
        if (_disposed)
        {
            return;
        }

        _watchdog.Stop();
        ReleaseSource();
        LastError = reason;

        var delay = RetryDelays[Math.Min(_failedAttempts, RetryDelays.Length - 1)];
        _failedAttempts++;
        SetStatus(_failedAttempts > RetryDelays.Length ? StreamStatus.Failed : StreamStatus.Reconnecting);

        _retryTimer.Interval = delay;
        _retryTimer.Start();
    }

    private void CheckForStall()
    {
        if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            _lastPlayingUtc = DateTime.UtcNow;
        }
        else if (DateTime.UtcNow - _lastPlayingUtc > StallTimeout)
        {
            ScheduleReconnect(Localizer.Get("StreamStopped"));
        }
    }

    private void OnMediaOpened(MediaPlayer sender, object args) => OnUiThread(() =>
    {
        _failedAttempts = 0;
        LastError = null;
        SetStatus(StreamStatus.Live);
    });

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var message = string.IsNullOrWhiteSpace(args.ErrorMessage) ? args.Error.ToString() : args.ErrorMessage;
        OnUiThread(() => ScheduleReconnect(message));
    }

    private void OnMediaEnded(MediaPlayer sender, object args) =>
        OnUiThread(() => ScheduleReconnect(Localizer.Get("StreamEnded")));

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        var state = sender.PlaybackState;
        OnUiThread(() =>
        {
            switch (state)
            {
                case MediaPlaybackState.Playing:
                    _lastPlayingUtc = DateTime.UtcNow;
                    SetStatus(StreamStatus.Live);
                    break;
                case MediaPlaybackState.Buffering when Status == StreamStatus.Live:
                    SetStatus(StreamStatus.Buffering);
                    break;
            }
        });
    }

    private void OnUiThread(Action action) => _dispatcher.TryEnqueue(() =>
    {
        if (!_disposed)
        {
            action();
        }
    });

    private void SetStatus(StreamStatus status)
    {
        if (Status != status)
        {
            Status = status;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReleaseSource()
    {
        if (_source is null)
        {
            return;
        }

        _player.Source = null;
        _source.Dispose();
        _source = null;

        if (_relayUrl is not null)
        {
            _proxy?.Unregister(_relayUrl);
            _relayUrl = null;
        }

        _listener?.Dispose();
        _listener = null;
        // The next connection has not been heard yet, rather than heard a long time ago.
        _lastSoundUtc = default;

        SetMetadata(null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connectCts?.Cancel();
        _watchdog.Stop();
        _retryTimer.Stop();
        _songEndTimer.Stop();

        _player.MediaOpened -= OnMediaOpened;
        _player.MediaFailed -= OnMediaFailed;
        _player.MediaEnded -= OnMediaEnded;
        _player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;

        ReleaseSource();
        _player.Dispose();
    }
}
