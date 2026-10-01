using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapperRadio.Core.Playback;

/// <summary>
/// When in the hour a station usually has its breaks. Most stations run to a fixed clock: the news on the hour, ads
/// before it and around half past. Every minute of the hour keeps how long the station was heard at that minute and
/// how much of that was a break (an ad break or speech, what the zapper leaves), so after some hours of listening
/// the minutes it usually breaks at stand out, whatever hour of the day they were heard in.
/// The minutes are those of the hour in UTC, so what was learned stays right when the time zone or summer time
/// changes; <see cref="Shares"/> and <see cref="UsualBreaks"/> turn them into the minutes of a local clock.
/// </summary>
public sealed class BreakClock
{
    public const int MinutesPerHour = 60;

    /// <summary>How many hours a station has to be heard before its clock is used and shown.</summary>
    public const double MinHours = 6;

    /// <summary>How long a minute of the hour has to be heard, in minutes, before what was heard of it counts.</summary>
    public const double MinMinutesHeard = 3;

    /// <summary>
    /// How often a minute has to be a break to be one of the usual break minutes. Breaks move by a minute or two from
    /// hour to hour, so a minute of a real break slot is not a break every hour; two hours out of five is plenty.
    /// </summary>
    public const double BreakShare = 0.4;

    /// <summary>
    /// How much listening it takes for what was heard to count half. It goes by the hours heard rather than by the
    /// days passed, so a clock is not forgotten while the app is closed, yet follows a station that changes its clock.
    /// </summary>
    public static readonly TimeSpan HalfLife = TimeSpan.FromHours(48);

    /// <summary>How far ahead a usual break counts as due: about the song a zap lands in.</summary>
    public static readonly TimeSpan LookAhead = TimeSpan.FromMinutes(3);

    private readonly double[] _heard = new double[MinutesPerHour];
    private readonly double[] _inBreak = new double[MinutesPerHour];

    /// <summary>How many hours the station was heard, with the older hours counting less.</summary>
    public double HoursHeard => _heard.Sum() / MinutesPerHour;

    /// <summary>Whether the station has been heard long enough for its clock to be used.</summary>
    public bool IsLearned => HoursHeard >= MinHours;

    /// <summary>Notes what the station did for <paramref name="length"/> at <paramref name="at"/>: a break or a song.</summary>
    public void Record(DateTimeOffset at, TimeSpan length, bool isBreak)
    {
        if (length <= TimeSpan.Zero)
        {
            return;
        }

        var fade = Math.Pow(0.5, length / HalfLife);
        for (var minute = 0; minute < MinutesPerHour; minute++)
        {
            _heard[minute] *= fade;
            _inBreak[minute] *= fade;
        }

        var index = at.UtcDateTime.Minute;
        _heard[index] += length.TotalMinutes;
        if (isBreak)
        {
            _inBreak[index] += length.TotalMinutes;
        }
    }

    /// <summary>How often the station breaks at a minute of the UTC hour, or null while that minute is not known well enough.</summary>
    public double? ShareAt(int utcMinute) =>
        _heard[utcMinute] >= MinMinutesHeard ? _inBreak[utcMinute] / _heard[utcMinute] : null;

    /// <summary>Whether a minute of the UTC hour is one the station usually breaks at.</summary>
    public bool IsBreakMinute(int utcMinute) => IsLearned && ShareAt(utcMinute) >= BreakShare;

    /// <summary>Whether one of the usual break minutes falls between now and <see cref="LookAhead"/> from now.</summary>
    public bool IsBreakDue(DateTimeOffset now)
    {
        var first = now.UtcDateTime.Minute;
        var minutes = (int)LookAhead.TotalMinutes;
        for (var step = 0; step <= minutes; step++)
        {
            if (IsBreakMinute((first + step) % MinutesPerHour))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How often the station breaks at each minute of the local hour, null where it is not known yet.</summary>
    /// <param name="utcOffset">The offset of the local clock, of which only the minutes matter.</param>
    public double?[] Shares(TimeSpan utcOffset)
    {
        var shares = new double?[MinutesPerHour];
        for (var minute = 0; minute < MinutesPerHour; minute++)
        {
            shares[ToLocal(minute, utcOffset)] = ShareAt(minute);
        }

        return shares;
    }

    /// <summary>
    /// The runs of usual break minutes on the local clock, each as its first minute and its length; a run may go past
    /// the hour, such as the five minutes from :58 to :02. Empty while the clock is not learned.
    /// </summary>
    public IReadOnlyList<(int Start, int Length)> UsualBreaks(TimeSpan utcOffset)
    {
        var isBreak = new bool[MinutesPerHour];
        for (var minute = 0; minute < MinutesPerHour; minute++)
        {
            isBreak[ToLocal(minute, utcOffset)] = IsBreakMinute(minute);
        }

        var count = isBreak.Count(b => b);
        if (count == MinutesPerHour)
        {
            return [(0, MinutesPerHour)];
        }

        // Start the walk right after a minute without a break, so a run across the hour is not cut in two.
        var origin = Array.IndexOf(isBreak, false) + 1;
        var runs = new List<(int Start, int Length)>();
        for (var step = 0; step < MinutesPerHour; step++)
        {
            var minute = (origin + step) % MinutesPerHour;
            if (!isBreak[minute])
            {
                continue;
            }

            if (runs.Count > 0 && (runs[^1].Start + runs[^1].Length) % MinutesPerHour == minute)
            {
                runs[^1] = (runs[^1].Start, runs[^1].Length + 1);
            }
            else
            {
                runs.Add((minute, 1));
            }
        }

        return runs.OrderBy(r => r.Start).ToList();
    }

    private static int ToLocal(int utcMinute, TimeSpan utcOffset) =>
        ((utcMinute + utcOffset.Minutes) % MinutesPerHour + MinutesPerHour) % MinutesPerHour;

    public BreakClockData ToData() => new(
        _heard.Select(m => Math.Round(m, 3)).ToArray(),
        _inBreak.Select(m => Math.Round(m, 3)).ToArray());

    /// <summary>A clock from what was saved; a file that does not fit gives an empty clock rather than an error.</summary>
    public static BreakClock FromData(BreakClockData data)
    {
        var clock = new BreakClock();
        if (data.Heard is { Length: MinutesPerHour } heard && data.InBreak is { Length: MinutesPerHour } inBreak)
        {
            for (var minute = 0; minute < MinutesPerHour; minute++)
            {
                clock._heard[minute] = Math.Max(0, heard[minute]);
                clock._inBreak[minute] = Math.Clamp(inBreak[minute], 0, clock._heard[minute]);
            }
        }

        return clock;
    }
}

/// <summary>What a <see cref="BreakClock"/> keeps, in minutes heard and minutes of break per minute of the UTC hour.</summary>
public sealed record BreakClockData(double[] Heard, double[] InBreak);

/// <summary>
/// Keeps the break clocks of the favorites in their own file next to the settings: they change every few seconds
/// while the app runs, which is far too often to rewrite the settings for.
/// </summary>
public sealed class BreakClockStore(string path)
{
    public Dictionary<string, BreakClock> Load()
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var data = JsonSerializer.Deserialize(stream, BreakClockJsonContext.Default.DictionaryStringBreakClockData) ?? [];
                return data.ToDictionary(d => d.Key, d => BreakClock.FromData(d.Value), StringComparer.Ordinal);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Learning the clocks again takes some hours; that is no reason to stop the app from starting.
        }

        return new Dictionary<string, BreakClock>(StringComparer.Ordinal);
    }

    public void Save(IReadOnlyDictionary<string, BreakClock> clocks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            var data = clocks.ToDictionary(c => c.Key, c => c.Value.ToData(), StringComparer.Ordinal);
            JsonSerializer.Serialize(stream, data, BreakClockJsonContext.Default.DictionaryStringBreakClockData);
        }

        File.Move(temp, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(Dictionary<string, BreakClockData>))]
internal sealed partial class BreakClockJsonContext : JsonSerializerContext;
