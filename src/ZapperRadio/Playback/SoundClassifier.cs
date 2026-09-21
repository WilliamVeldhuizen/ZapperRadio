using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ZapperRadio.Core.Audio;

namespace ZapperRadio.Playback;

/// <summary>
/// Hears whether streams play music or speech, with YAMNet, Google's sound classifier (see Assets/Models),
/// and how loud they are, with <see cref="Loudness"/>.
/// Each stream's audio is taken from the relay as it is passed to the player, so it costs no extra bandwidth.
/// Every 5 seconds of it is decoded and classified, one stream at a time on a single background thread,
/// which takes about 20 ms per stream.
/// </summary>
public sealed class SoundClassifier : IDisposable
{
    private const int SampleRate = 16000;
    private const int SpeechClass = 0;
    private const int MusicClass = 132;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    /// <summary>Only the latest audio of a longer window is classified, such as the burst a station sends on connect.</summary>
    private const int MaxSamples = 10 * SampleRate;

    /// <summary>More than a window of even 320 kbit/s audio; beyond this the audio is dropped rather than piling up.</summary>
    private const int MaxBufferedBytes = 1024 * 1024;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly List<Listener> _listeners = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private SoundClassifier(InferenceSession session)
    {
        _session = session;
        _inputName = session.InputMetadata.Keys.First();
        _loop = Task.Run(RunAsync);
    }

    /// <summary>Loads the model, or returns null when it cannot be used; ad breaks are then judged without listening.</summary>
    public static SoundClassifier? TryCreate(string modelPath)
    {
        try
        {
            MediaFoundationApi.Startup();
            return new SoundClassifier(new InferenceSession(modelPath));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts listening to a stream: write its audio to the listener, and <paramref name="onWindow"/> is called on a
    /// background thread with the sound and the loudness of every window. Dispose the listener to stop.
    /// </summary>
    public Listener Listen(Action<SoundWindow> onWindow)
    {
        var listener = new Listener(this, onWindow);
        lock (_listeners)
        {
            _listeners.Add(listener);
        }

        return listener;
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                List<Listener> due;
                lock (_listeners)
                {
                    due = _listeners.Where(l => l.IsDue).ToList();
                }

                foreach (var listener in due)
                {
                    var audio = listener.Take();
                    if (audio.Length == 0 || _stop.IsCancellationRequested)
                    {
                        continue;
                    }

                    try
                    {
                        var samples = Decode(audio);
                        var (sound, speechFrom) = Classify(samples);
                        // Only the music of a station says how loud it is mastered, so nothing else is measured.
                        var loudness = sound == Sound.Music ? Loudness.Measure(samples, SampleRate) : null;
                        listener.Report(new SoundWindow(sound, loudness, speechFrom));
                    }
                    catch (Exception)
                    {
                        // A window that cannot be decoded, for example right after a reconnect; the next one will do.
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Decodes MP3 or AAC audio with Media Foundation, which finds the first frame by itself.</summary>
    private static float[] Decode(byte[] audio)
    {
        using var reader = new StreamMediaFoundationReader(new MemoryStream(audio));
        var samples = reader.ToSampleProvider();
        if (samples.WaveFormat.Channels == 2)
        {
            samples = new StereoToMonoSampleProvider(samples);
        }

        if (samples.WaveFormat.Channels != 1)
        {
            return [];
        }

        var mono = new WdlResamplingSampleProvider(samples, SampleRate);
        var result = new List<float>(MaxSamples);
        var buffer = new float[SampleRate];
        int read;
        while ((read = mono.Read(buffer, 0, buffer.Length)) > 0)
        {
            result.AddRange(buffer.AsSpan(0, read));
        }

        return result.Count > MaxSamples ? result[^MaxSamples..].ToArray() : result.ToArray();
    }

    /// <returns>What the window sounds like, and for speech how far into it the talking begins.</returns>
    private (Sound Sound, double SpeechFrom) Classify(float[] samples)
    {
        // YAMNet needs just under a second for a single frame.
        if (samples.Length < SampleRate)
        {
            return (Sound.Unknown, 0);
        }

        var input = NamedOnnxValue.CreateFromTensor(_inputName, new DenseTensor<float>(samples, [samples.Length]));
        using var results = _session.Run([input]);
        var scores = results.First().AsTensor<float>();
        var frames = scores.Dimensions[0];
        if (frames == 0)
        {
            return (Sound.Unknown, 0);
        }

        var speech = new float[frames];
        var music = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            speech[frame] = scores[frame, SpeechClass];
            music[frame] = scores[frame, MusicClass];
        }

        var sound = SoundHistory.Label(speech.Average(), music.Average());
        return (sound, sound == Sound.Speech ? SoundHistory.SpeechFrom(speech, music) : 0);
    }

    public void Dispose()
    {
        _stop.Cancel();
        // A window still being classified must not lose its model halfway; the process is ending anyway.
        if (_loop.Wait(TimeSpan.FromSeconds(2)))
        {
            _session.Dispose();
        }
    }

    public sealed class Listener : IDisposable
    {
        private readonly SoundClassifier _owner;
        private readonly Action<SoundWindow> _onWindow;
        private readonly Lock _gate = new();
        private MemoryStream _audio = new();
        private DateTime _windowStartUtc = DateTime.UtcNow;
        private bool _disposed;

        internal Listener(SoundClassifier owner, Action<SoundWindow> onWindow)
        {
            _owner = owner;
            _onWindow = onWindow;
        }

        internal bool IsDue
        {
            get
            {
                lock (_gate)
                {
                    return DateTime.UtcNow - _windowStartUtc >= Window;
                }
            }
        }

        /// <summary>Adds audio as it streams in. Safe to call from any thread.</summary>
        public void Write(ReadOnlyMemory<byte> audio)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (_audio.Length + audio.Length > MaxBufferedBytes)
                {
                    _audio.SetLength(0);
                }

                _audio.Write(audio.Span);
            }
        }

        internal byte[] Take()
        {
            lock (_gate)
            {
                var audio = _audio.ToArray();
                _audio.SetLength(0);
                _windowStartUtc = DateTime.UtcNow;
                return audio;
            }
        }

        internal void Report(SoundWindow window)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            _onWindow(window);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _audio = new MemoryStream();
            }

            lock (_owner._listeners)
            {
                _owner._listeners.Remove(this);
            }
        }
    }
}
