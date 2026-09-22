using System.Text;
using ZapperRadio.Core.Streaming;

namespace ZapperRadio.Core.Tests;

public class AudioDurationTests
{
    /// <summary>MPEG-1 layer III at 128 kbit/s and 44.1 kHz: 417 bytes, or 418 with padding, of 1152 samples.</summary>
    private static byte[] Mp3Frame(bool padding = false)
    {
        var frame = new byte[padding ? 418 : 417];
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        frame[2] = (byte)(padding ? 0x92 : 0x90);
        return frame;
    }

    /// <summary>AAC-LC in ADTS at 44.1 kHz, stereo: one block of 1024 samples.</summary>
    private static byte[] AdtsFrame(int length = 372)
    {
        var frame = new byte[length];
        frame[0] = 0xFF;
        frame[1] = 0xF1;
        frame[2] = 0x50;
        frame[3] = (byte)(0x80 | (length >> 11));
        frame[4] = (byte)(length >> 3);
        frame[5] = (byte)(((length & 7) << 5) | 0x1F);
        frame[6] = 0xFC;
        return frame;
    }

    private static byte[] Repeat(Func<int, byte[]> frame, int count) =>
        Enumerable.Range(0, count).SelectMany(frame).ToArray();

    private static TimeSpan Measure(byte[] stream, int chunk)
    {
        var duration = new AudioDuration();
        for (var i = 0; i < stream.Length; i += chunk)
        {
            duration.Add(stream.AsSpan(i, Math.Min(chunk, stream.Length - i)));
        }

        return duration.Duration;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(16 * 1024)]
    public void CountsTheFramesOfAnMp3Stream(int chunk)
    {
        // The last frame counts once the one after it comes in.
        var stream = Repeat(i => Mp3Frame(padding: i % 3 == 0), 200);

        Assert.Equal(199 * 1152 / 44100.0, Measure(stream, chunk).TotalSeconds, 6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public void CountsTheFramesOfAnAacStream(int chunk)
    {
        var stream = Repeat(i => AdtsFrame(360 + i % 20), 100);

        Assert.Equal(99 * 1024 / 44100.0, Measure(stream, chunk).TotalSeconds, 6);
    }

    [Fact]
    public void FindsTheFirstFrameInAConnectionThatStartsHalfwayIntoOne()
    {
        var stream = Mp3Frame()[150..].Concat(Repeat(_ => Mp3Frame(), 10)).ToArray();

        Assert.Equal(9 * 1152 / 44100.0, Measure(stream, 64).TotalSeconds, 6);
    }

    [Fact]
    public void DoesNotPassOverFramesForASyncWordInTheAudioData()
    {
        // The start of an ADTS header that claims 2047 bytes, which would reach over the first five real frames.
        var partial = Mp3Frame()[150..];
        new byte[] { 0xFF, 0xF1, 0x50, 0x80, 0xFF, 0xE0, 0xFC }.CopyTo(partial, 100);
        var stream = partial.Concat(Repeat(_ => Mp3Frame(), 10)).ToArray();

        Assert.Equal(9 * 1152 / 44100.0, Measure(stream, 64).TotalSeconds, 6);
    }

    [Fact]
    public void FindsTheFramesBackAfterData()
    {
        var junk = new byte[] { 0xFF, 0xFB, 0x00, 0xFF, 0xFF, 0xF1, 0x12, 0x34 };
        var stream = Repeat(_ => Mp3Frame(), 10).Concat(junk).Concat(Repeat(_ => Mp3Frame(), 10)).ToArray();

        // The frame before the junk and the last one wait for a frame after them that does not come.
        Assert.Equal(18 * 1152 / 44100.0, Measure(stream, 7).TotalSeconds, 6);
    }

    [Fact]
    public void CountsNothingInAnotherFormat()
    {
        var stream = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("OggS not an mp3 frame ÿ ", 500)));

        Assert.Equal(TimeSpan.Zero, Measure(stream, 100));
    }
}
