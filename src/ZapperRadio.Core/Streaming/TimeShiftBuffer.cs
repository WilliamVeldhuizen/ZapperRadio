using System.Runtime.InteropServices;

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

    /// <summary>
    /// How much of a new connection is compared with what the buffer already holds, to find where it joins. A fraction
    /// of a second of any stream, and long enough that the same bytes elsewhere in the audio would be a coincidence.
    /// </summary>
    public const int JoinProbeLength = 2048;

    /// <summary>How far back a new connection is looked for: past the longest burst a server sends on connect.</summary>
    private static readonly TimeSpan JoinSearch = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long a new connection is held back at most. Where it joins is known within the burst it starts with, which
    /// comes in at once; a server that sends the station seconds behind the previous connection would otherwise hold the
    /// buffer still for all those seconds, and a replay close to the live edge with it.
    /// </summary>
    private static readonly TimeSpan JoinHold = TimeSpan.FromSeconds(3);

    private readonly Lock _gate = new();

    /// <summary>Oldest first. The first mark may lie before <see cref="_start"/>, when the ring overwrote part of its stretch.</summary>
    private readonly List<Mark> _marks = [];

    /// <summary>The playing time of everything appended, from the frames of MP3 and AAC; other formats stay at zero.</summary>
    private readonly AudioDuration _audio = new();

    private TimeSpan _length;
    private byte[] _ring = [];
    private long _start;
    private long _end;
    private DateTimeOffset _lastAppend;
    private TaskCompletionSource _appended = NewSignal();

    /// <summary>The start of a new connection, held back until it is known where it joins the audio before it; null once it is.</summary>
    private List<byte>? _join;

    /// <summary>The position of the buffer the new connection seems to start at, or null while that is not known.</summary>
    private long? _joinAt;

    /// <summary>Where in <see cref="_join"/> the stretch begins that is being followed in the buffer from <see cref="_joinAt"/> on.</summary>
    private int _joinFrom;

    /// <summary>How many bytes of that stretch were found to be the same as the buffer.</summary>
    private int _joinVerified;

    /// <summary>When the first audio of the new connection came in.</summary>
    private DateTimeOffset _joinStarted;

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
    /// bitrate asks for a size a good deal different; otherwise the audio from before a reconnect is kept, and the new
    /// connection joins it where it carries on. Some servers announce a slightly different bitrate on every connection
    /// (Qmusic says 95 or 96), which is no reason to throw away what is kept. Most servers start a connection with the
    /// last 10 to 30 seconds of the station, which the buffer mostly holds already: appended as it comes, a replay would
    /// hear those seconds twice, and play the rest of the station that much later than the time it came in says.
    /// </summary>
    public void Connect(string? contentType, int? bitrateKbps)
    {
        lock (_gate)
        {
            ContentType = contentType;
            BitrateKbps = bitrateKbps;
            var capacity = CapacityFor(_length, bitrateKbps);
            if (_ring.Length == 0 || Math.Abs(capacity - _ring.Length) > _ring.Length / 4)
            {
                Allocate(capacity);
            }

            _joinAt = null;
            _joinFrom = 0;
            _join = _end > _start ? [] : null;
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
        _join = null;
        _joinAt = null;
        _joinFrom = 0;
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

            if (_join is not null)
            {
                if (_join.Count == 0)
                {
                    _joinStarted = now;
                }

                _join.AddRange(audio);
                audio = Join();
                if (_join is not null && now - _joinStarted > JoinHold)
                {
                    audio = EndJoin(CollectionsMarshal.AsSpan(_join));
                }
            }

            if (audio.IsEmpty)
            {
                return;
            }

            Store(audio, now);
            appended = _appended;
            _appended = NewSignal();
        }

        appended.TrySetResult();
    }

    /// <summary>
    /// Where a new connection carries on the audio before it. The start of what it sent is looked for in what the buffer
    /// holds, and followed from there for as long as the two are the same. Where they part before the end of the buffer,
    /// the rest is looked for again, because some servers first send the burst they sent on the previous connect, and
    /// only then the last seconds. Once the new connection runs past the end of the buffer, everything before that is
    /// held already and left out, and the rest is new.
    /// A connection that does not get there is taken as it is, even when it started with bytes the buffer holds: a
    /// station that starts every connection with the same pre-roll ad sends it again, and its title says so, so leaving
    /// the ad out would have that title fall on the station's music.
    /// </summary>
    /// <returns>What of the new connection can be stored now; nothing while that is not known yet.</returns>
    private byte[] Join()
    {
        var pending = CollectionsMarshal.AsSpan(_join);
        while (true)
        {
            var rest = pending[_joinFrom..];
            if (_joinAt is null)
            {
                if (rest.Length < JoinProbeLength)
                {
                    return [];
                }

                var from = Math.Max(_start, _end - CapacityFor(JoinSearch, BitrateKbps));
                var kept = new byte[_end - from];
                Copy(from, kept);
                var at = kept.AsSpan().LastIndexOf(rest[..JoinProbeLength]);
                if (at < 0)
                {
                    return EndJoin(pending);
                }

                _joinAt = from + at;
                _joinVerified = 0;
            }

            var shared = _end - _joinAt.Value;
            var compared = (int)Math.Min(shared, rest.Length);
            if (compared > _joinVerified)
            {
                var kept = new byte[compared - _joinVerified];
                Copy(_joinAt.Value + _joinVerified, kept);
                var same = kept.AsSpan().CommonPrefixLength(rest[_joinVerified..compared]);
                if (same < kept.Length)
                {
                    // Held up to here, from further back than the end; what follows may be held too.
                    _joinFrom += _joinVerified + same;
                    _joinAt = null;
                    continue;
                }

                _joinVerified = compared;
            }

            return rest.Length <= shared ? [] : EndJoin(rest[(int)shared..]);
        }
    }

    private byte[] EndJoin(ReadOnlySpan<byte> store)
    {
        var result = store.ToArray();
        _join = null;
        _joinAt = null;
        _joinFrom = 0;
        return result;
    }

    private void Store(ReadOnlySpan<byte> audio, DateTimeOffset now)
    {
        if (_marks.Count == 0 || now - _marks[^1].Time >= MarkInterval)
        {
            _marks.Add(new Mark(_end, now, _audio.Duration));
        }

        _audio.Add(audio);
        _lastAppend = now;

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
    }

    /// <summary>Copies the audio from <paramref name="position"/> on into <paramref name="destination"/>.</summary>
    /// <returns>The bytes copied: 0 when there is nothing newer yet, -1 when the position is no longer kept.</returns>
    public int Read(long position, Span<byte> destination)
    {
        lock (_gate)
        {
            return position < _start ? -1 : Copy(position, destination);
        }
    }

    private int Copy(long position, Span<byte> destination)
    {
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
    /// The playing time of the audio from the start of the buffer's count up to <paramref name="position"/>, or null
    /// when the position is not kept. Only differences between two of these mean anything; in a format whose frames
    /// are not counted it stays at zero.
    /// </summary>
    public TimeSpan? AudioAt(long position)
    {
        lock (_gate)
        {
            return CountedAt(position);
        }
    }

    private TimeSpan? CountedAt(long position)
    {
        if (position < _start || position > _end || _marks.Count == 0)
        {
            return null;
        }

        var end = EndMark();
        var i = _marks.FindLastIndex(m => m.Position <= position);
        var mark = _marks[Math.Max(i, 0)];
        var next = i + 1 < _marks.Count ? _marks[i + 1] : end;
        return next.Position == mark.Position
            ? mark.Audio
            : mark.Audio + (next.Audio - mark.Audio) * ((double)(position - mark.Position) / (next.Position - mark.Position));
    }

    /// <summary>
    /// When the audio came in that plays at <paramref name="audio"/> in the count of <see cref="AudioAt"/>: the moment of
    /// the broadcast that a player hears once it has played that far. Unlike counting the time it has been playing,
    /// this stays right where the audio did not come in at the pace it plays, such as the burst a server sends on
    /// connect. Null when the buffer does not reach back that far, or when its frames are not counted.
    /// </summary>
    public DateTimeOffset? TimeOfAudio(TimeSpan audio)
    {
        lock (_gate)
        {
            var end = EndMark();
            if (_marks.Count == 0 || end.Audio == _marks[0].Audio || audio < _marks[0].Audio)
            {
                return null;
            }

            if (audio >= end.Audio)
            {
                // Played past what is counted, which the frame still being counted can be; it plays in real time.
                return end.Time + (audio - end.Audio);
            }

            var i = _marks.FindLastIndex(m => m.Audio <= audio);
            var mark = _marks[i];
            var next = i + 1 < _marks.Count ? _marks[i + 1] : end;
            // The audio of a mark's stretch came in within an interval of it; the next mark can be long after that,
            // when the station paused.
            var span = next.Time - mark.Time < MarkInterval ? next.Time - mark.Time : MarkInterval;
            return next.Audio == mark.Audio
                ? mark.Time
                : mark.Time + span * ((audio - mark.Audio) / (next.Audio - mark.Audio));
        }
    }

    /// <summary>
    /// How much playing time the buffer holds from <paramref name="position"/> up to the live edge: from the frames, or
    /// from the bitrate in a format whose frames are not counted.
    /// </summary>
    public TimeSpan AheadOf(long position)
    {
        lock (_gate)
        {
            if (position >= _end)
            {
                return TimeSpan.Zero;
            }

            if (CountedAt(position) is { } at && _audio.Duration > at)
            {
                return _audio.Duration - at;
            }

            return TimeSpan.FromSeconds((_end - position) * 8.0 / ((BitrateKbps ?? FallbackKbps) * 1000));
        }
    }

    /// <summary>Where the audio ends now, as a mark.</summary>
    private Mark EndMark() => new(_end, _lastAppend, _audio.Duration);

    /// <summary>
    /// The time of the oldest byte. When the ring overwrote the start of the first mark's stretch, the oldest byte
    /// came in somewhere between that mark and the next, and the next is the bound that is known for certain.
    /// </summary>
    private DateTimeOffset? OldestTime() =>
        _end == _start || _marks.Count == 0 ? null
        : _marks[0].Position >= _start || _marks.Count == 1 ? _marks[0].Time
        : _marks[1].Time;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>A position, when the audio there came in, and how much playing time came before it.</summary>
    private readonly record struct Mark(long Position, DateTimeOffset Time, TimeSpan Audio);
}
