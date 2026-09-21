using System.Text.Json;
using System.Text.Json.Serialization;
using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Settings;

public sealed class AppSettings
{
    /// <summary>Favorites keep streaming (muted) in the background, so their number is capped.</summary>
    public const int MaxFavorites = 20;

    public List<Station> Favorites { get; set; } = [];

    /// <summary>Songs saved from the station being listened to, newest first.</summary>
    public List<FavoriteTrack> FavoriteTracks { get; set; } = [];

    public double Volume { get; set; } = 0.8;

    public string? Country { get; set; }

    /// <summary>The language tag chosen for the app, e.g. "nl-NL", or null to follow the language of Windows.</summary>
    public string? Language { get; set; }

    /// <summary>Zap to another favorite during the ad breaks of the station being listened to, and back afterwards.</summary>
    public bool ZappOnAdBreaks { get; set; }

    /// <summary>Whether every station is brought to the same loudness, so zapping does not change the volume.</summary>
    public bool NormalizeLoudness { get; set; } = true;

    /// <summary>The lengths the time-shift buffer can be set to, in minutes; 0 plays every station live.</summary>
    public static readonly IReadOnlyList<int> TimeShiftChoices = [0, 2, 5, 10];

    /// <summary>
    /// How many minutes of every favorite are kept, so a zap can start the song on the other station from its
    /// beginning. Five covers nearly every song a zap lands in.
    /// </summary>
    public int TimeShiftMinutes { get; set; } = 5;

    /// <summary>
    /// The loudness in LUFS measured per station, by stream URL, so the correction applies from the first second
    /// of the next run instead of after the minute of music it takes to measure it again.
    /// </summary>
    public Dictionary<string, double> StationLoudness { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Whether the small window with only the favorites is shown instead of the full one.</summary>
    public bool IsCompact { get; set; }

    /// <summary>Whether the Ctrl+Alt shortcuts also work while another app has focus.</summary>
    public bool GlobalHotkeys { get; set; } = true;

    /// <summary>Where the full window was last left, so switching back to it returns it there.</summary>
    public WindowPlacement? FullWindow { get; set; }

    /// <summary>Where the compact window was last left; it is remembered apart from the full one.</summary>
    public WindowPlacement? CompactWindow { get; set; }

    /// <summary>What <see cref="ZappOnAdBreaks"/> was called before; only read, so older settings files keep the choice.</summary>
    [JsonPropertyName("SkipAdBreaks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacySkipAdBreaks
    {
        get => null;
        set
        {
            if (value is true)
            {
                ZappOnAdBreaks = true;
            }
        }
    }
}

/// <summary>Where a window was, in the physical pixels the Windows App SDK positions windows in.</summary>
public sealed class WindowPlacement
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>
    /// Whether the window was maximized. The size above is the one it had before that, which is what Windows
    /// restores it to, so switching to the other view and back can put it back the way it was left.
    /// </summary>
    public bool IsMaximized { get; set; }
}

public sealed class SettingsStore(string path)
{
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt settings file should not prevent the app from starting.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, settings, SettingsJsonContext.Default.AppSettings);
        }

        File.Move(temp, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, IgnoreReadOnlyProperties = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
