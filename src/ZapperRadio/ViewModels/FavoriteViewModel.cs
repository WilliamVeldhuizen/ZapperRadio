using CommunityToolkit.Mvvm.ComponentModel;
using ZapperRadio.Core.Audio;
using ZapperRadio.Core.Models;
using ZapperRadio.Core.Playback;
using ZapperRadio.Playback;

namespace ZapperRadio.ViewModels;

public sealed partial class FavoriteViewModel(Station station) : ObservableObject
{
    public Station Station { get; } = station;

    public StationLogoViewModel Logo { get; } = new() { Initials = StationLogoViewModel.ComputeInitials(station.Name) };

    public string Name => Station.Name;

    /// <summary>Whether the mouse is over the row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    public partial bool IsPointerOver { get; set; }

    /// <summary>Whether the row, or a button in it, has the keyboard focus.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    public partial bool HasFocus { get; set; }

    /// <summary>Whether the row shows its buttons, which stay out of the way until the row is pointed at or focused.</summary>
    public bool ShowActions => IsPointerOver || HasFocus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ShowIconIndicator))]
    [NotifyPropertyChangedFor(nameof(ShowDotIndicator))]
    public partial StreamStatus Status { get; set; } = StreamStatus.Connecting;

    /// <summary>Whether the station plays music or speech right now, as far as it is heard.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ShowIconIndicator))]
    [NotifyPropertyChangedFor(nameof(ShowDotIndicator))]
    public partial Sound Sound { get; set; }

    /// <summary>The song the station is playing right now, or empty when it does not say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSong))]
    [NotifyPropertyChangedFor(nameof(HasNoSong))]
    public partial string Song { get; set; } = "";

    public bool HasSong => Song.Length > 0;

    /// <summary>Whether the favorites show the status instead, because the station does not say what it plays.</summary>
    public bool HasNoSong => !HasSong;

    /// <summary>Whether the station marks an ad break right now, which is shown in red.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIconIndicator))]
    [NotifyPropertyChangedFor(nameof(ShowDotIndicator))]
    public partial bool IsAd { get; set; }

    /// <summary>Whether an ad break is only assumed, which is shown in yellow.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIconIndicator))]
    [NotifyPropertyChangedFor(nameof(ShowDotIndicator))]
    public partial bool IsAssumedAdBreak { get; set; }

    /// <summary>
    /// Whether the indicator of a favorite shows an icon instead of a plain dot: what is heard (a note for music, a
    /// speech bubble for talking) while the station is live and not in an ad break, or a network icon while the
    /// stream is connecting, buffering or reconnecting.
    /// </summary>
    public bool ShowIconIndicator =>
        Status is StreamStatus.Connecting or StreamStatus.Buffering or StreamStatus.Reconnecting
        || (Status == StreamStatus.Live && !IsAd && !IsAssumedAdBreak && Sound is Sound.Music or Sound.Speech);

    public bool ShowDotIndicator => !ShowIconIndicator;

    public string StatusText => StatusTexts.For(Status, IsActive, Sound);

    /// <summary>Whether a break may be zapped to this station; off for one that is no place to wait for the music, such as the news.</summary>
    [ObservableProperty]
    public partial bool CanZapTo { get; set; } = true;

    /// <summary>Raised when <see cref="CanZapTo"/> is switched.</summary>
    public event EventHandler? CanZapToChanged;

    partial void OnCanZapToChanged(bool value) => CanZapToChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>What was measured of this station's loudness, as the settings show it.</summary>
    [ObservableProperty]
    public partial string LoudnessText { get; set; } = LoudnessTexts.NotMeasured;

    /// <summary>
    /// How often the station breaks at each minute of the local hour, as the Zapper tab draws it; NaN where it is not
    /// known yet, because the bindings of the XAML compiler cannot take an array of nullable numbers.
    /// </summary>
    [ObservableProperty]
    public partial double[] BreakShares { get; set; } = Unknown();

    /// <summary>When in the hour the station usually breaks, or how far it is with learning that.</summary>
    [ObservableProperty]
    public partial string BreakClockText { get; set; } = BreakClockTexts.For(new BreakClock(), TimeSpan.Zero);

    /// <summary>Shows what was learned of when the station breaks, on the local clock.</summary>
    public void ShowBreakClock(BreakClock clock, TimeSpan utcOffset)
    {
        BreakShares = clock.IsLearned ? clock.Shares(utcOffset).Select(s => s ?? double.NaN).ToArray() : Unknown();
        BreakClockText = BreakClockTexts.For(clock, utcOffset);
    }

    private static double[] Unknown() => Enumerable.Repeat(double.NaN, BreakClock.MinutesPerHour).ToArray();
}

/// <summary>What the Zapper tab says about when a station usually breaks.</summary>
public static class BreakClockTexts
{
    /// <summary>
    /// Such as "Usually a break at :58–:02, :28–:31", or how many of the hours it takes have been heard. The minutes
    /// read the same in every language, so only the sentence around them is translated.
    /// </summary>
    public static string For(BreakClock clock, TimeSpan utcOffset)
    {
        if (!clock.IsLearned)
        {
            return Localizer.Format("BreakClockLearning", (int)clock.HoursHeard, (int)BreakClock.MinHours);
        }

        var breaks = clock.UsualBreaks(utcOffset);
        if (breaks.Count == 0)
        {
            return Localizer.Get("BreakClockNone");
        }

        // A talk station, or one that hardly plays a song: a list of minutes would say less than this.
        if (breaks.Sum(b => b.Length) >= BreakClock.MinutesPerHour * 3 / 4)
        {
            return Localizer.Get("BreakClockMostOfTheHour");
        }

        return Localizer.Format("BreakClockUsual", string.Join(", ", breaks.Select(Range)));
    }

    private static string Range((int Start, int Length) run) =>
        run.Length == 1 ? $":{run.Start:00}" : $":{run.Start:00}–:{(run.Start + run.Length - 1) % BreakClock.MinutesPerHour:00}";
}

public static class SongTexts
{
    /// <summary>What a stream plays at a moment: for the station being listened to, the moment that is heard.</summary>
    public static string For(StreamMoment moment) => moment switch
    {
        { Metadata.IsAd: true } => Localizer.Get("SongAdvertisement"),
        { IsAssumedAdBreak: true } => Localizer.Get("SongProbablyAdBreak"),
        // Stations that shout their whole library are toned down, so the list does not shout along.
        { Metadata.Title: { } title } => TrackTitle.Normalize(title),
        _ => "",
    };
}

/// <summary>What the loudness section of the settings shows per station.</summary>
public static class LoudnessTexts
{
    public static string NotMeasured => Localizer.Get("LoudnessNotMeasured");

    /// <summary>The numbers are the same in every language the app might be read in, so they are not localized.</summary>
    private static readonly System.Globalization.CultureInfo Numbers = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>
    /// The loudness of a station and what is done about it, e.g. "-9.3 LUFS · turned down 4.7 dB". The correction
    /// is left out while it is switched off, because the measurement is still worth showing. While the station is
    /// measured again, the old numbers are still the ones in use, and the text says another measurement is coming.
    /// </summary>
    public static string For(double? loudness, double gainDb, bool normalize, bool remeasuring = false)
    {
        if (loudness is not { } measured)
        {
            return NotMeasured;
        }

        var level = string.Format(Numbers, "{0:0.0} LUFS", measured);
        if (normalize && Math.Abs(gainDb) >= 0.05)
        {
            var db = Math.Abs(gainDb).ToString("0.0", Numbers);
            level += " · " + Localizer.Format(gainDb < 0 ? "LoudnessTurnedDown" : "LoudnessTurnedUp", db);
        }

        return remeasuring ? level + " · " + Localizer.Get("LoudnessMeasuringAgain") : level;
    }
}

public static class StatusTexts
{
    public static string For(StreamStatus status, bool isActive, Sound sound) => status switch
    {
        StreamStatus.Connecting => Localizer.Get("StatusConnecting"),
        StreamStatus.Live => Localizer.Get(isActive ? "StatusNowPlaying" : "StatusLiveMuted") + SoundSuffix(sound),
        StreamStatus.Buffering => Localizer.Get("StatusBuffering"),
        StreamStatus.Reconnecting => Localizer.Get("StatusReconnecting"),
        StreamStatus.Failed => Localizer.Get("StatusFailed"),
        _ => "",
    };

    private static string SoundSuffix(Sound sound) => sound switch
    {
        Sound.Music => " · " + Localizer.Get("SoundMusic"),
        Sound.Speech => " · " + Localizer.Get("SoundSpeech"),
        _ => "",
    };
}
