using System.Runtime.InteropServices;

namespace ZapperRadio.Core.Streaming;

/// <summary>
/// Counts how much playing time a stream of MP3 or AAC (ADTS) audio holds, from the headers of its frames, as the
/// bytes come in. A bitrate cannot do this: a stream announced as 128 kbit/s rarely comes to exactly that, and over
/// an hour a percent off is half a minute. A frame says exactly how many samples it holds and at what rate.
/// A frame counts once the next one is found where it should begin. Until two frames in a row are found, such as
/// in a connection that starts halfway into a frame, the bytes are kept and searched, so a sync word that happens
/// to be in the audio data is not taken for a frame and cannot make it pass over real ones.
/// Not thread-safe.
/// </summary>
public sealed class AudioDuration
{
    private const int MpegHeaderLength = 4;
    private const int AdtsHeaderLength = 7;

    /// <summary>More than the longest frame (8 KB of ADTS) and the header after it; older bytes are given up on.</summary>
    private const int MaxSearched = 16 * 1024;

    private static readonly int[] MpegSampleRates = [44100, 48000, 32000];

    private static readonly int[] AdtsSampleRates = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];

    /// <summary>Kbit/s per bitrate index 1 to 14: MPEG-1 layer I, II and III, then MPEG-2 and 2.5 layer I, and II and III.</summary>
    private static readonly int[][] MpegBitrates =
    [
        [32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448],
        [32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384],
        [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320],
        [32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256],
        [8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160],
    ];

    private readonly byte[] _header = new byte[AdtsHeaderLength];
    private readonly List<byte> _searched = [];
    private int _headerLength;
    private long _skip;
    private double _seconds;

    /// <summary>The length of the frame being passed over, counted once the frame after it is found; null while the frames are lost.</summary>
    private double? _pending;

    /// <summary>The playing time of the frames counted so far.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(_seconds);

    /// <summary>Adds the next bytes of the stream.</summary>
    public void Add(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            if (_pending is null)
            {
                Search(data);
                return;
            }

            if (_skip > 0)
            {
                var passed = (int)Math.Min(_skip, data.Length);
                data = data[passed..];
                _skip -= passed;
                continue;
            }

            _header[_headerLength++] = data[0];
            data = data[1..];
            var header = _header.AsSpan(0, _headerLength);
            if (_headerLength >= 2 && HeaderLength(header) is { } length && _headerLength < length)
            {
                continue;
            }

            if (_headerLength >= 2 && Parse(header) is { } frame)
            {
                _seconds += _pending.Value;
                _pending = frame.Seconds;
                _skip = frame.Length - _headerLength;
            }
            else if (_headerLength >= 2 || _header[0] != 0xFF)
            {
                // Not the frame that should be here: lost, until two frames in a row are found again.
                _pending = null;
                _searched.AddRange(header);
            }
            else
            {
                continue;
            }

            _headerLength = 0;
        }
    }

    /// <summary>Looks for a frame that the next frame follows, in what came in since the frames were lost.</summary>
    private void Search(ReadOnlySpan<byte> data)
    {
        _searched.AddRange(data);
        var bytes = CollectionsMarshal.AsSpan(_searched);
        var at = 0;
        for (; bytes.Length - at >= 2; at++)
        {
            if (FrameAt(bytes, at) is not { } frame)
            {
                continue;
            }

            if (frame.Length == 0)
            {
                // A header that is not complete yet, or whose next frame has not come in: wait for more.
                break;
            }

            var next = at + frame.Length;
            if (FrameAt(bytes, next) is not { } following)
            {
                continue;
            }

            if (following.Length == 0)
            {
                break;
            }

            _pending = frame.Seconds;
            var rest = bytes[next..].ToArray();
            _searched.Clear();
            Add(rest);
            return;
        }

        _searched.RemoveRange(0, at);
        if (_searched.Count > MaxSearched)
        {
            _searched.RemoveRange(0, _searched.Count - MaxSearched);
        }
    }

    /// <summary>The frame whose header is at <paramref name="at"/>, null when there is none, or of length 0 when there are too few bytes to tell.</summary>
    private static (int Length, double Seconds)? FrameAt(ReadOnlySpan<byte> bytes, int at)
    {
        if (bytes.Length - at < 2)
        {
            return (0, 0);
        }

        var start = bytes[at..];
        if (HeaderLength(start) is not { } length)
        {
            return null;
        }

        return start.Length < length ? (0, 0) : Parse(start[..length]);
    }

    /// <summary>How long the header that begins with these two bytes is, or null when they are no sync word.</summary>
    private static int? HeaderLength(ReadOnlySpan<byte> header) =>
        header[0] != 0xFF || (header[1] & 0xE0) != 0xE0 ? null
        : IsAdts(header) ? AdtsHeaderLength
        : (header[1] & 0x06) != 0 ? MpegHeaderLength
        : null;

    /// <summary>ADTS has the layer bits at zero, where MPEG audio has a layer.</summary>
    private static bool IsAdts(ReadOnlySpan<byte> header) => (header[1] & 0xF6) == 0xF0;

    private static (int Length, double Seconds)? Parse(ReadOnlySpan<byte> header) =>
        HeaderLength(header) is not { } length || header.Length < length ? null
        : IsAdts(header) ? ParseAdts(header)
        : ParseMpeg(header);

    private static (int Length, double Seconds)? ParseMpeg(ReadOnlySpan<byte> header)
    {
        var version = (header[1] >> 3) & 3; // 0 = MPEG-2.5, 1 = reserved, 2 = MPEG-2, 3 = MPEG-1
        var layer = 4 - ((header[1] >> 1) & 3); // 1, 2 or 3; 4 is reserved
        var bitrateIndex = header[2] >> 4;
        var rateIndex = (header[2] >> 2) & 3;
        if (version == 1 || layer == 4 || bitrateIndex is 0 or 15 || rateIndex == 3)
        {
            return null;
        }

        var mpeg1 = version == 3;
        var table = mpeg1 ? layer - 1 : layer == 1 ? 3 : 4;
        var bitrate = MpegBitrates[table][bitrateIndex - 1] * 1000;
        var sampleRate = MpegSampleRates[rateIndex] >> (mpeg1 ? 0 : version == 2 ? 1 : 2);
        var padding = (header[2] >> 1) & 1;
        var samples = layer == 1 ? 384 : layer == 3 && !mpeg1 ? 576 : 1152;
        var length = layer == 1 ? (12 * bitrate / sampleRate + padding) * 4 : samples / 8 * bitrate / sampleRate + padding;
        return (length, (double)samples / sampleRate);
    }

    private static (int Length, double Seconds)? ParseAdts(ReadOnlySpan<byte> header)
    {
        var rateIndex = (header[2] >> 2) & 0xF;
        var length = ((header[3] & 3) << 11) | (header[4] << 3) | (header[5] >> 5);
        if (rateIndex >= AdtsSampleRates.Length || length < AdtsHeaderLength)
        {
            return null;
        }

        // HE-AAC signals half its output rate here, with half the samples per frame, so the length comes out the same.
        var blocks = (header[6] & 3) + 1;
        return (length, 1024.0 * blocks / AdtsSampleRates[rateIndex]);
    }
}
