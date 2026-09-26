using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;
using Windows.System.UserProfile;

namespace ZapperRadio;

/// <summary>A language the app can be shown in. A null <see cref="Tag"/> stands for "the language of Windows".</summary>
public sealed record AppLanguage(string? Tag, string Name);

/// <summary>
/// Picks the language of the app and looks up its texts. The texts of the XAML are found through <c>x:Uid</c>
/// and the texts built in code through <see cref="Get"/>, both in <c>Strings\&lt;language&gt;\Resources.resw</c>.
/// The language is chosen once at the start and stays for the whole run: a window and its bindings are not
/// redrawn in another language, so changing it in the settings takes effect the next time the app starts.
/// </summary>
public static class Localizer
{
    /// <summary>English, and the language the texts fall back to when one is missing from another.</summary>
    public const string Fallback = "en-US";

    /// <summary>The languages there is a <c>Resources.resw</c> for, each named in itself so it can be found from any other.</summary>
    public static IReadOnlyList<AppLanguage> Languages { get; } =
    [
        new("en-US", "English"),
        new("zh-CN", "中文 (简体)"),
        new("es-ES", "Español"),
        new("pt-BR", "Português (Brasil)"),
        new("fr-FR", "Français"),
        new("de-DE", "Deutsch"),
        new("ja-JP", "日本語"),
        new("uk-UA", "Українська"),
        new("it-IT", "Italiano"),
        new("nl-NL", "Nederlands"),
        new("pl-PL", "Polski"),
        new("tr-TR", "Türkçe"),
        new("ko-KR", "한국어"),
        new("zh-TW", "中文 (繁體)"),
        new("ru-RU", "Русский"),
        new("cs-CZ", "Čeština"),
        new("sv-SE", "Svenska"),
        new("id-ID", "Bahasa Indonesia"),
        new("hu-HU", "Magyar"),
        new("ro-RO", "Română"),
        new("da-DK", "Dansk"),
        new("nb-NO", "Norsk (bokmål)"),
        new("fi-FI", "Suomi"),
        new("el-GR", "Ελληνικά"),
        new("sk-SK", "Slovenčina"),
        new("vi-VN", "Tiếng Việt"),
        new("th-TH", "ไทย"),
        new("hi-IN", "हिन्दी"),
        new("ar-SA", "العربية"),
        new("he-IL", "עברית"),
    ];

    private static ResourceLoader? _loader;

    /// <summary>The language the app is shown in, as one of the tags in <see cref="Languages"/>.</summary>
    public static string Current { get; private set; } = Fallback;

    /// <summary>Whether <see cref="Current"/> is written from right to left (Arabic, Hebrew), so the window is mirrored.</summary>
    public static bool IsRightToLeft => CultureInfo.GetCultureInfo(Current).TextInfo.IsRightToLeft;

    /// <summary>
    /// Switches the app to <paramref name="preferred"/>, or to the first language of Windows that is available
    /// when it is null or unknown. Dates and numbers follow, so a window reads in one language throughout.
    /// Has to run before the first window is created, because that is when the XAML looks up its texts.
    /// </summary>
    public static void Use(string? preferred)
    {
        Current = Languages.Any(l => l.Tag == preferred) ? preferred! : FromWindows();
        ApplicationLanguages.PrimaryLanguageOverride = Current;

        var culture = CultureInfo.GetCultureInfo(Current);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        _loader = new ResourceLoader();
    }

    /// <summary>The text with this name in the language of the app; the name itself when there is none, so a gap is visible.</summary>
    public static string Get(string name)
    {
        var text = _loader?.GetString(name);
        return string.IsNullOrEmpty(text) ? name : text;
    }

    /// <summary>The text with this name, with <paramref name="args"/> put in the places marked <c>{0}</c>, <c>{1}</c> and so on.</summary>
    public static string Format(string name, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(name), args);

    /// <summary>The first language in the list of Windows that the app has texts for, or English when none of them is.</summary>
    private static string FromWindows()
    {
        foreach (var tag in GlobalizationPreferences.Languages)
        {
            if (Match(tag) is { } match)
            {
                return match;
            }
        }

        return Fallback;
    }

    /// <summary>
    /// The language of the app that a Windows language falls under: "nl-BE" is Dutch and "pt-PT" is Portuguese.
    /// Chinese is the one where the country matters: simplified characters (zh-Hans, zh-CN, zh-SG) and
    /// traditional ones (zh-Hant, zh-TW, zh-HK, zh-MO) are not read by everyone who asks for the other.
    /// Norwegian comes as "nb", "nn" or plain "no", and all of them read the bokmål texts.
    /// </summary>
    private static string? Match(string windowsTag)
    {
        var parts = windowsTag.Split('-');
        if (parts[0].Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            var simplified = parts.Length == 1 || parts.Any(p => p.Equals("Hans", StringComparison.OrdinalIgnoreCase))
                             || (!parts.Any(p => p.Equals("Hant", StringComparison.OrdinalIgnoreCase))
                                 && !parts.Any(p => p.Equals("TW", StringComparison.OrdinalIgnoreCase) || p.Equals("HK", StringComparison.OrdinalIgnoreCase) || p.Equals("MO", StringComparison.OrdinalIgnoreCase)));
            return simplified ? "zh-CN" : "zh-TW";
        }

        var language = parts[0].ToLowerInvariant() is "nn" or "no" ? "nb" : parts[0];
        return Languages.FirstOrDefault(l => l.Tag!.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase))?.Tag;
    }
}
