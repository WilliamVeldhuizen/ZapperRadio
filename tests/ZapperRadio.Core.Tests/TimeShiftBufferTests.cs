using ZapperRadio.Core.Streaming;

namespace ZapperRadio.Core.Tests;

public class TimeShiftBufferTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsSizedInTimeFromTheBitrate()
    {
        var buffer = new TimeShiftBuffer(TimeSpan.FromMinutes(5));
        Assert.Equal(0, buffer.Capacity);

        buffer.Connect("audio/mpeg", 128);

        // 5 minutes of 128 kbit/s is 4.8 MB, as the roadmap worked out.
        Assert.Equal(4_800_000, buffer.Capacity);
        Assert.Equal(12_000_000, TimeShiftBuffer.CapacityFor(TimeSpan.FromMinutes(5), 320));
        Assert.Equal(TimeShiftBuffer.CapacityFor(TimeSpan.FromMinutes(5), TimeShiftBuffer.FallbackKbps), TimeShiftBuffer.CapacityFor(TimeSpan.FromMinutes(5), null));
    }

    [Theory]
    [InlineData("128", 128)]
    [InlineData("128,128", 128)]
    [InlineData(" 64 ", 64)]
    [InlineData("0", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParsesTheIcyBitrate(string? header, int? kbps)
    {
        Assert.Equal(kbps, TimeShiftBuffer.ParseBitrate(header));
    }

    [Fact]
    public void KeepsTheAudioAcrossAReconnect()
    {
        var buffer = Connected(capacity: 100);
        buffer.Append("abc"u8, T0);

        buffer.Connect("audio/mpeg", 8);

        Assert.Equal("abc", Read(buffer, 0, 10));
    }

    [Fact]
    public void KeepsTheAudioWhenAReconnectAnnouncesABitrateJustAsDifferent()
    {
        var buffer = new TimeShiftBuffer(TimeSpan.FromMinutes(5));
        buffer.Connect("audio/aacp", 96);
        buffer.Append("abc"u8, T0);

        buffer.Connect("audio/aacp", 95);
        Assert.Equal("abc", Read(buffer, 0, 10));

        buffer.Connect("audio/aacp", 320);
        Assert.Equal(TimeShiftBuffer.CapacityFor(TimeSpan.FromMinutes(5), 320), buffer.Capacity);
    }

    [Fact]
    public void GivesUpJoiningAConnectionThatDoesNotCatchUp()
    {
        var station = Station(20_000);
        var buffer = Connected(capacity: 100_000);
        buffer.Append(station[..10_000], T0);

        // A server that sends the station seconds behind the previous connection: it matches, but slowly.
        buffer.Connect("audio/mpeg", 8);
        buffer.Append(station.AsSpan(2000, 3000), T0.AddSeconds(10));
        Assert.Equal(10_000, buffer.End);
        buffer.Append(station.AsSpan(5000, 1000), T0.AddSeconds(14));

        Assert.Equal(14_000, buffer.End);
    }

    [Fact]
    public void JoinsAReconnectWhereItCarriesOn()
    {
        var station = Station(10_000);
        var buffer = Connected(capacity: 100_000);
        buffer.Append(station[..6000], T0);

        // The server starts the new connection with the last 3 seconds, most of which the buffer holds already.
        buffer.Connect("audio/mpeg", 8);
        for (var i = 3000; i < station.Length; i += 1000)
        {
            buffer.Append(station.AsSpan(i, 1000), T0.AddSeconds(10));
        }

        Assert.Equal(station.Length, buffer.End);
        Assert.Equal(station, ReadBytes(buffer, 0, station.Length));
    }

    [Fact]
    public void SkipsABurstOfOnlyOldAudio()
    {
        var station = Station(10_000);
        var buffer = Connected(capacity: 100_000);
        buffer.Append(station[..8000], T0);

        buffer.Connect("audio/mpeg", 8);
        buffer.Append(station.AsSpan(2000, 3000), T0.AddSeconds(10));
        Assert.Equal(8000, buffer.End);
        buffer.Append(station.AsSpan(5000, 5000), T0.AddSeconds(11));

        Assert.Equal(station, ReadBytes(buffer, 0, station.Length));
    }

    [Fact]
    public void HoldsANewConnectionBackUntilItIsKnownWhereItJoins()
    {
        var station = Station(10_000);
        var buffer = Connected(capacity: 100_000);
        buffer.Append(station[..6000], T0);

        buffer.Connect("audio/mpeg", 8);
        buffer.Append(station.AsSpan(3000, 1000), T0.AddSeconds(10));

        Assert.Equal(6000, buffer.End);
    }

    [Fact]
    public void JoinsAConnectionThatFirstRepeatsTheBurstOfTheConnectionBefore()
    {
        var station = Station(12_000);
        var buffer = Connected(capacity: 100_000);
        // The first connection started with a burst of 3000 bytes.
        buffer.Append(station[..8000], T0);

        // The server sends that burst again, then the last seconds, then what is new.
        buffer.Connect("audio/mpeg", 8);
        buffer.Append(station.AsSpan(0, 3000), T0.AddSeconds(10));
        buffer.Append(station.AsSpan(5000, 7000), T0.AddSeconds(10));

        Assert.Equal(station, ReadBytes(buffer, 0, station.Length));
    }

    [Fact]
    public void DoesNotJoinAConnectionThatOnlyStartsTheSame()
    {
        // Every connection starts with the same pre-roll ad, and then the station as it is now.
        var preRoll = Station(3000, seed: 1);
        var before = Station(5000, seed: 2);
        var after = Station(5000, seed: 3);
        var buffer = Connected(capacity: 100_000);
        buffer.Append(preRoll, T0);
        buffer.Append(before, T0.AddSeconds(1));

        buffer.Connect("audio/mpeg", 8);
        buffer.Append(preRoll, T0.AddSeconds(10));
        buffer.Append(after, T0.AddSeconds(11));

        Assert.Equal(preRoll.Concat(before).Concat(preRoll).Concat(after).ToArray(), ReadBytes(buffer, 0, 16_000));
    }

    [Fact]
    public void TakesAConnectionThatSharesNothingAsItIs()
    {
        var before = Station(6000, seed: 1);
        var after = Station(3000, seed: 2);
        var buffer = Connected(capacity: 100_000);
        buffer.Append(before, T0);

        buffer.Connect("audio/mpeg", 8);
        buffer.Append(after, T0.AddSeconds(10));

        Assert.Equal(before.Concat(after).ToArray(), ReadBytes(buffer, 0, 9000));
    }

    [Fact]
    public void DatesTheAudioByWhenItCameInRatherThanByItsPlayingTime()
    {
        var frames = Mp3Frames(20);
        var buffer = Connected(capacity: 100_000);
        // Half a second of audio in one burst, and the next half second ten seconds later.
        buffer.Append(frames, T0);
        buffer.Append(frames, T0.AddSeconds(10));

        // The frame at the end of the burst counts once the frame after it has come in.
        Assert.Equal((19 * Mp3FrameLength).TotalSeconds, buffer.AudioAt(frames.Length)!.Value.TotalSeconds, 6);
        Assert.Equal(TimeSpan.Zero, buffer.AudioAt(0));
        // Within the interval of the mark the burst came in at, not spread over the ten seconds after it.
        Assert.InRange(buffer.TimeOfAudio(10 * Mp3FrameLength)!.Value, T0, T0 + TimeShiftBuffer.MarkInterval);
        // A player that played 30 frames from the start hears the second stretch, not a moment 0.8 seconds in.
        Assert.Equal(T0.AddSeconds(10), buffer.TimeOfAudio(30 * Mp3FrameLength));
    }

    [Fact]
    public void DatesNoAudioInAFormatWhoseFramesAreNotCounted()
    {
        var buffer = Connected(capacity: 100);
        buffer.Append("abcd"u8, T0);

        Assert.Null(buffer.TimeOfAudio(TimeSpan.Zero));
    }

    [Fact]
    public void AnotherLengthThrowsAwayWhatWasKept()
    {
        var buffer = Connected(capacity: 100);
        buffer.Append("abc"u8, T0);

        buffer.Resize(TimeSpan.FromMilliseconds(200));

        Assert.Equal(200, buffer.Capacity);
        Assert.Equal(3, buffer.Start);
        Assert.Equal(-1, buffer.Read(0, new byte[10]));
        Assert.Null(buffer.StartTime);
    }

    [Fact]
    public void WrapsAroundAndDropsTheOldestAudio()
    {
        var buffer = Connected(capacity: 8);
        buffer.Append("abcdef"u8, T0);
        buffer.Append("ghij"u8, T0.AddSeconds(1));

        Assert.Equal(2, buffer.Start);
        Assert.Equal(10, buffer.End);
        Assert.Equal("cdefghij", Read(buffer, 2, 100));
        Assert.Equal("hij", Read(buffer, 7, 100));
        Assert.Equal(-1, buffer.Read(1, new byte[4]));
        Assert.Equal(0, buffer.Read(10, new byte[4]));
    }

    [Fact]
    public void KeepsOnlyTheTailOfAChunkLargerThanTheRing()
    {
        var buffer = Connected(capacity: 4);
        buffer.Append("abcdefgh"u8, T0);

        Assert.Equal(4, buffer.Start);
        Assert.Equal("efgh", Read(buffer, 4, 100));
    }

    [Fact]
    public void FindsAMomentOfTheBroadcast()
    {
        var buffer = Connected(capacity: 100);
        buffer.Append("aaaa"u8, T0);
        buffer.Append("bbbb"u8, T0.AddSeconds(10));
        buffer.Append("cccc"u8, T0.AddSeconds(20));

        Assert.Equal(4, buffer.PositionAt(T0.AddSeconds(10)));
        // Within a stretch, the audio of the mark before it.
        Assert.Equal(4, buffer.PositionAt(T0.AddSeconds(15)));
        Assert.Equal(8, buffer.PositionAt(T0.AddSeconds(25)));
        Assert.Equal(T0.AddSeconds(10), buffer.TimeAt(6));
        Assert.Equal(T0, buffer.StartTime);
    }

    [Fact]
    public void DoesNotReachBackBeforeWhatItKeeps()
    {
        var buffer = Connected(capacity: 8);
        buffer.Append("aaaa"u8, T0);
        buffer.Append("bbbb"u8, T0.AddSeconds(10));
        buffer.Append("cc"u8, T0.AddSeconds(20));

        // The first stretch is partly overwritten, so the oldest audio that is known for certain is the second.
        Assert.Equal(T0.AddSeconds(10), buffer.StartTime);
        Assert.Null(buffer.PositionAt(T0.AddSeconds(5)));
        Assert.Equal(4, buffer.PositionAt(T0.AddSeconds(10)));
        Assert.Null(new TimeShiftBuffer(TimeSpan.FromMinutes(5)).PositionAt(T0));
    }

    [Fact]
    public void MarksAtMostOncePerInterval()
    {
        var buffer = Connected(capacity: 100);
        buffer.Append("aa"u8, T0);
        buffer.Append("bb"u8, T0.AddMilliseconds(50));
        buffer.Append("cc"u8, T0 + TimeShiftBuffer.MarkInterval);

        Assert.Equal(0, buffer.PositionAt(T0.AddMilliseconds(100)));
        Assert.Equal(4, buffer.PositionAt(T0 + TimeShiftBuffer.MarkInterval));
    }

    [Fact]
    public async Task WaitsForAudioToComeIn()
    {
        var buffer = Connected(capacity: 100);
        var waiting = buffer.WaitForDataAsync(0, CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        buffer.Append("a"u8, T0);

        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(buffer.WaitForDataAsync(0, CancellationToken.None).IsCompleted);
    }

    [Fact]
    public async Task ReplayPlaysFromAPositionAndFollowsTheStation()
    {
        var buffer = Connected(capacity: 100);
        buffer.Append("old-"u8, T0);
        buffer.Append("song"u8, T0.AddSeconds(30));
        var output = new MemoryStream();
        using var cts = new CancellationTokenSource();

        var replay = IcyProxy.ReplayAsync(output, buffer, buffer.PositionAt(T0.AddSeconds(30))!.Value, isHead: false, cts.Token);
        buffer.Append("-next"u8, T0.AddSeconds(31));
        await WaitUntil(() => output.ToArray().AsSpan().EndsWith("-next"u8));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replay);

        var text = System.Text.Encoding.ASCII.GetString(output.ToArray());
        Assert.StartsWith("HTTP/1.1 200 OK\r\nContent-Type: audio/mpeg\r\n", text);
        Assert.EndsWith("\r\n\r\nsong-next", text);
    }

    [Fact]
    public async Task ReplayWaitsUntilTheBufferReachesItsLead()
    {
        var buffer = Connected(capacity: 100_000);
        buffer.Append(Mp3Frames(20), T0);
        var output = new MemoryStream();
        using var cts = new CancellationTokenSource();

        // Half a second of audio is there; the replay wants a second before it starts.
        var replay = IcyProxy.ReplayAsync(output, buffer, 0, isHead: false, cts.Token, lead: TimeSpan.FromSeconds(1));
        await Task.Delay(100);
        var head = output.Length;
        Assert.True(head > 0);

        buffer.Append(Mp3Frames(30), T0.AddSeconds(1));
        await WaitUntil(() => output.Length > head);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replay);
        Assert.Equal(head + 50 * 417, output.Length);
    }

    [Fact]
    public async Task ProxyKeepsTheRelayedAudioAndReplaysIt()
    {
        using var upstream = new HttpClient(new StreamHandler(() =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent("abcdefghij"u8.ToArray()) };
            response.Content.Headers.ContentType = new("audio/aac");
            response.Headers.Add("icy-br", "64");
            return response;
        }));
        using var proxy = new IcyProxy(upstream);
        var buffer = new TimeShiftBuffer(TimeSpan.FromMinutes(1));
        var url = proxy.Register(new Uri("http://radio.example/live.aac"), _ => { }, buffer: buffer);

        using var client = new HttpClient();
        Assert.Equal("abcdefghij"u8.ToArray(), await client.GetByteArrayAsync(url));

        Assert.Equal(64, buffer.BitrateKbps);
        Assert.Equal("audio/aac", buffer.ContentType);
        Assert.Equal(480_000, buffer.Capacity);
        Assert.Equal("abcdefghij", Read(buffer, 0, 100));

        var replayUrl = proxy.RegisterReplay(buffer, 3, new Uri("http://radio.example/live.aac"));
        Assert.EndsWith("/live.aac", replayUrl.AbsolutePath);
        using var response = await client.GetAsync(replayUrl, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("audio/aac", response.Content.Headers.ContentType?.MediaType);
        await using var body = await response.Content.ReadAsStreamAsync();
        var received = new byte[7];
        await body.ReadExactlyAsync(received).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("defghij"u8.ToArray(), received);
        proxy.Unregister(replayUrl);
    }

    /// <summary>A buffer of exactly <paramref name="capacity"/> bytes: a millisecond per byte at 8 kbit/s.</summary>
    private static TimeShiftBuffer Connected(int capacity)
    {
        var buffer = new TimeShiftBuffer(TimeSpan.FromMilliseconds(capacity));
        buffer.Connect("audio/mpeg", 8);
        return buffer;
    }

    /// <summary>Bytes that do not repeat, standing in for a station's audio.</summary>
    private static byte[] Station(int length, int seed = 0)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>The playing time of one MPEG-1 layer III frame at 44.1 kHz.</summary>
    private static readonly TimeSpan Mp3FrameLength = TimeSpan.FromSeconds(1152 / 44100.0);

    /// <summary>Frames of 128 kbit/s MP3, 417 bytes each.</summary>
    private static byte[] Mp3Frames(int count) =>
        Enumerable.Range(0, count).SelectMany(_ => new byte[] { 0xFF, 0xFB, 0x90 }.Concat(new byte[414])).ToArray();

    private static byte[] ReadBytes(TimeShiftBuffer buffer, long position, int count)
    {
        var bytes = new byte[count];
        return bytes[..Math.Max(buffer.Read(position, bytes), 0)];
    }

    private static string Read(TimeShiftBuffer buffer, long position, int count)
    {
        var bytes = new byte[count];
        var read = buffer.Read(position, bytes);
        return System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Max(read, 0));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class StreamHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }
}
