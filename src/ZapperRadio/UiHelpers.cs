using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ZapperRadio.Core.Audio;
using ZapperRadio.Playback;

namespace ZapperRadio;

/// <summary>Functions used from x:Bind expressions.</summary>
public static class UiHelpers
{
    private static readonly SolidColorBrush LiveBrush = new(ColorHelper.FromArgb(255, 16, 185, 90));
    private static readonly SolidColorBrush BusyBrush = new(ColorHelper.FromArgb(255, 234, 162, 30));
    private static readonly SolidColorBrush FailedBrush = new(ColorHelper.FromArgb(255, 220, 60, 60));
    private static readonly SolidColorBrush StarOnBrush = new(ColorHelper.FromArgb(255, 245, 184, 0));
    private static readonly SolidColorBrush HeartOnBrush = new(ColorHelper.FromArgb(255, 232, 64, 87));
    private static readonly SolidColorBrush StarOffBrush = new(ColorHelper.FromArgb(255, 138, 138, 138));

    public static Brush StatusBrush(StreamStatus status) => status switch
    {
        StreamStatus.Live => LiveBrush,
        StreamStatus.Failed => FailedBrush,
        _ => BusyBrush,
    };

    /// <summary>The accent color for a song, red during an ad break, or yellow when an ad break is only assumed.</summary>
    public static Brush SongBrush(bool isAd, bool isAssumedAdBreak) =>
        isAd ? FailedBrush
        : isAssumedAdBreak ? StarOnBrush
        : (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];

    /// <summary>The small indicator of a favorite in the compact view: green while it is live, yellow while it is
    /// connecting or an ad break is only assumed, and red during an ad break or when the stream will not play.</summary>
    public static Brush IndicatorBrush(StreamStatus status, bool isAd, bool isAssumedAdBreak) =>
        isAd || status == StreamStatus.Failed ? FailedBrush
        : isAssumedAdBreak || status != StreamStatus.Live ? BusyBrush
        : LiveBrush;

    /// <summary>Splits the green indicator: a note while the station plays music, a speech bubble while someone talks.</summary>
    public static string IndicatorGlyph(Sound sound) => Glyph(sound == Sound.Speech ? 0xE90A : 0xE8D6); // Comment / MusicNote

    public static string ViewGlyph(bool isCompact) => Glyph(isCompact ? 0xE740 : 0xE73F); // FullScreen / BackToWindow

    public static string ViewToolTip(bool isCompact) =>
        Localizer.Get(isCompact ? "ViewSwitchToFull" : "ViewSwitchToCompact");

    /// <summary>A plus on a search result that can be added to the favorites, and a check on one that already is.</summary>
    public static string FavoriteGlyph(bool isFavorite) => Glyph(isFavorite ? 0xE73E : 0xE710); // CheckMark / Add

    public static Brush FavoriteBrush(bool isFavorite) =>
        isFavorite ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"] : StarOffBrush;

    public static string FavoriteToolTip(bool isFavorite) => Localizer.Get(isFavorite ? "FavoriteRemove" : "FavoriteAdd");

    public static string HeartGlyph(bool isSaved) => Glyph(isSaved ? 0xE00B : 0xE006); // HeartFill / Heart

    public static Brush HeartBrush(bool isSaved) => isSaved ? HeartOnBrush : StarOffBrush;

    public static string SaveTrackToolTip(bool isSaved) => Localizer.Get(isSaved ? "SaveTrackRemove" : "SaveTrackAdd");

    public static string PlayGlyph(bool isPlaying) => Glyph(isPlaying ? 0xE71A : 0xE768); // Stop / Play

    public static string MuteGlyph(bool isMuted) => Glyph(isMuted ? 0xE74F : 0xE767); // Mute / Volume

    public static string MuteToolTip(bool isMuted) => Localizer.Get(isMuted ? "MuteToolTipUnmute" : "MuteToolTipMute");

    /// <summary>The name and version on the about section of the settings, e.g. "ZapperRadio 1.10.0".</summary>
    public static string AboutVersion(string version) => $"ZapperRadio {version}";

    private static string Glyph(int codePoint) => ((char)codePoint).ToString();
}
