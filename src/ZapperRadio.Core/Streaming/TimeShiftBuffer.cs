namespace ZapperRadio.Core.Streaming;

/// <summary>
/// The last few minutes of a station, as the compressed bytes the relay passes on, so a zap can start the song on
/// another station from its beginning instead of wherever the broadcast happens to be. Compressed audio is about a
/// tenth of the decoded PCM: five minutes of 128 kbit/s is 4.8 MB.
/// The buffer is sized in time rather than in bytes, from the bitrate the station announces, so a 320 kbit/s
/// station keeps as many minutes as a 128 kbit/s one. Its array is allocated once, when the stream first connects,
/// and reused across reconnects: every array over 85 KB lands on the Large Object Heap, which a ring buffer that is
/// created over and over would fragment.
/// Positions count every byte ever appended, so they keep their meaning while the ring wraps around, and each
/// stretch of audio is marked with the time it came in, which is how a moment of the broadcast is found back.
/// Every member is safe to call from any thread: the relay appends on its own, and a replay reads on another.
/// </summary>
public sealed class TimeShiftBuffer
{
    /// <summary>The bitrate assumed for a station that does not announce one: high, so it never keeps too little.</summary>
    public const int FallbackKbps = 320;

    /// <summary>How far apart the marks that tie positions to times are; finer than any cut the zapper makes.</summary>
    public static readonly TimeSpan MarkInterval = TimeSpan.FromMilliseconds(200);

    private readonly Lock _gate = new();

    /// <summary>Oldest first. The first mark may lie before <see cref="_start"/>, when the ring overwrote part of its stretch.</summary>
    private readonly List<Mark> _marks = [];

    private TimeSpan _length;
    private byte[] _ring = [];
    private long _start;
    private long _end;
    private TaskCompletionSource _appended = NewSignal();

    public TimeShiftBuffer(TimeSpan length)
    {
        _length = length;
    }

    /// <summary>How much of the station is kept; zero keeps nothing.</summary>
    public TimeSpan Length
    {
        get
        {
            lock (_gate)
            {
                return _length;
            }
        }
    }

    /// <summary>The bitrate the station announced in kbit/s, or null before it connected or when it does not say.</summary>
    public int? BitrateKbps { get; private set; }

    /// <summary>The media type of the audio, for whoever plays it back, or null before the stream connected.</summary>
    public string? ContentType { get; private set; }

    /// <summary>The bytes the ring holds once it is full.</summary>
    public int Capacity
    {
        get
        {
            lock (_gate)
            {
                return _ring.Length;
            }
        }
    }

    /// <summary>The position of the oldest byte still kept.</summary>
    public long Start
    {
        get
        {
            lock (_gate)
            {
                return _start;
            }
        }
    }

    /// <summary>The position after the newest byte: the live edge of the station.</summary>
    public long End
    {
        get
        {
            lock (_gate)
            {
                return _end;
            }
        }
    }

    /// <summary>When the oldest byte still kept came in, or null while nothing is kept.</summary>
    public DateTimeOffset? StartTime
    {
        get
        {
            lock (_gate)
            {
                return OldestTime();
            }
        }
    }

    /// <summary>The bytes a buffer of <paramref name="length"/> takes for a station at <paramref name="bitrateKbps"/>.</summary>
    public static int CapacityFor(TimeSpan length, int? bitrateKbps) =>
        (int)Math.Clamp(length.TotalSeconds * (bitrateKbps ?? FallbackKbps) * 1000 / 8, 0, int.MaxValue);

    /// <summary>
    /// Reads the bitrate from an <c>icy-br</c> header, which some servers send as a list such as "128,128".
    /// Anything outside what radio streams use is treated as unknown.
    /// </summary>
    public static int? ParseBitrate(string? header) =>
        header?.Split(',')[0].Trim() is { } first && int.TryParse(first, out var kbps) && kbps is >= 8 and <= 1536 ? kbps : null;

    /// <summary>
    /// Called by the relay when the station answers. Allocates the ring the first time, and again only when the
    /// bitrate asks for a different size; otherwise the audio from before a reconnect is kept.
    /// </summary>
    public void Connect(string? contentType, int? bitrateKbps)
    {
        lock (_gate)
        {
            ContentType = contentType;
            BitrateKbps = bitrateKbps;
            Allocate(CapacityFor(_length, bitrateKbps));
        }
    }

    /// <summary>Keeps a different length from now on. What was kept is thrown away, because the ring is replaced.</summary>
    public void Resize(TimeSpan length)
    {
        lock (_gate)
        {
            if (length == _length)
            {
                return;
            }

            _length = length;
            if (ContentType is not null)
            {
                Allocate(CapacityFor(length, BitrateKbps));
            }
        }
    }

    private void Allocate(int capacity)
    {
        if (capacity == _ring.Length)
        {
            return;
        }

        _ring = capacity > 0 ? new byte[capacity] : [];
        // Positions keep counting, so a replay that is still reading finds itself before the start and moves on.
        _start = _end;
        _marks.Clear();
    }

    /// <summary>Adds audio as it comes in from the station.</summary>
    public void Append(ReadOnlySpan<byte> audio, DateTimeOffset now)
    {
        TaskCompletionSource appended;
        lock (_gate)
        {
            if (_ring.Length == 0 || audio.IsEmpty)
            {
                return;
            }

            if (_marks.Count == 0 || now - _marks[^1].Time >= MarkInterval)
            {
                _marks.Add(new Mark(_end, now));
            }

            // Only the tail of a chunk larger than the whole ring would survive anyway.
            if (audio.Length > _ring.Length)
            {
                _end += audio.Length - _ring.Length;
                audio = audio[^_ring.Length..];
            }

            var offset = (int)(_end % _ring.Length);
            var first = Math.Min(audio.Length, _ring.Length - offset);
            audio[..first].CopyTo(_ring.AsSpan(offset));
            audio[first..].CopyTo(_ring);
            _end += audio.Length;
            _start = Math.Max(_start, _end - _ring.Length);

            // The marks whose whole stretch was overwritten go; the one the oldest byte belongs to stays.
            var gone = 0;
            while (gone + 1 < _marks.Count && _marks[gone + 1].Position <= _start)
            {
                gone++;
            }

            _marks.RemoveRange(0, gone);

            appended = _appended;
            _appended = NewSignal();
        }

        appended.TrySetResult();
    }

    /// <summary>Copies the audio from <paramref name="position"/> on into <paramref name="destination"/>.</summary>
    /// <returns>The bytes copied: 0 when there is nothing newer yet, -1 when the position is no longer kept.</returns>
    public int Read(long position, Span<byte> destination)
    {
        lock (_gate)
        {
            if (position < _start)
            {
                return -1;
            }

            var count = (int)Math.Min(destination.Length, _end - position);
            if (count <= 0)
            {
                return 0;
            }

            var offset = (int)(position % _ring.Length);
            var first = Math.Min(count, _ring.Length - offset);
            _ring.AsSpan(offset, first).CopyTo(destination);
            _ring.AsSpan(0, count - first).CopyTo(destination[first..]);
            return count;
        }
    }

    /// <summary>Completes once there is audio at or after <paramref name="position"/>.</summary>
    public Task WaitForDataAsync(long position, CancellationToken cancellationToken)
    {
        Task appended;
        lock (_gate)
        {
            if (position < _end)
            {
                return Task.CompletedTask;
            }

            appended = _appended.Task;
        }

        return appended.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// The position of the audio that came in at <paramref name="time"/>, or null when the buffer does not reach back
    /// that far, or when nothing came in after it yet.
    /// </summary>
    public long? PositionAt(DateTimeOffset time)
    {
        lock (_gate)
        {
            if (OldestTime() is not { } oldest || time < oldest)
            {
                return null;
            }

            // The last mark at or before the time. Within the 200 ms of one mark the audio came in evenly enough.
            var position = _start;
            foreach (var mark in _marks)
            {
                if (mark.Time > time)
                {
                    break;
                }

                position = Math.Max(mark.Position, _start);
            }

            return position < _end ? position : null;
        }
    }

    /// <summary>When the audio at <paramref name="position"/> came in, or null when it is not kept.</summary>
    public DateTimeOffset? TimeAt(long position)
    {
        lock (_gate)
        {
            if (position < _start || position >= _end)
            {
                return null;
            }

            var time = _marks[0].Time;
            foreach (var mark in _marks)
            {
                if (mark.Position > position)
                {
                    break;
                }

                time = mark.Time;
            }

            return OldestTime() is { } oldest && time < oldest ? oldest : time;
        }
    }

    /// <summary>
    /// The time of the oldest byte. When the ring overwrote the start of the first mark's stretch, the oldest byte
    /// came in somewhere between that mark and the next, and the next is the bound that is known for certain.
    /// </summary>
    private DateTimeOffset? OldestTime() =>
        _end == _start || _marks.Count == 0 ? null
        : _marks[0].Position >= _start || _marks.Count == 1 ? _marks[0].Time
        : _marks[1].Time;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly record struct Mark(long Position, DateTimeOffset Time);
}
