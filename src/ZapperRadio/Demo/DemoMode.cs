using ZapperRadio.Core.Audio;
using ZapperRadio.Core.Models;
using ZapperRadio.Core.Playback;
using ZapperRadio.Core.Settings;

namespace ZapperRadio.Demo;

/// <summary>
/// The demo mode, started with <c>ZapperRadio.exe --demo</c>, or <c>--demo=nl-NL</c> to show it in another language:
/// the real window filled with made-up stations, logos and songs, so screenshots of the app show nobody else's brand.
/// Nothing streams. It keeps its settings and history in a folder of its own, which it fills afresh on every start,
/// so the real favorites and history are never touched.
/// </summary>
public static class DemoMode
{
    private const string Argument = "--demo";

    static DemoMode()
    {
        foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
        {
            if (arg.Equals(Argument, StringComparison.OrdinalIgnoreCase))
            {
                IsOn = true;
            }
            else if (arg.StartsWith(Argument + "=", StringComparison.OrdinalIgnoreCase))
            {
                IsOn = true;
                Language = arg[(Argument.Length + 1)..];
            }
        }
    }

    public static bool IsOn { get; }

    /// <summary>The language the demo is shown in, or null for the language of Windows.</summary>
    public static string? Language { get; }

    /// <summary>The favorite being listened to: the zapper just left the ad break of the one above it.</summary>
    public static Station Listening => Favorites[1].Station;

    /// <summary>How far the station being listened to plays behind live, since the zap started its song from the beginning.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(48);

    public static IReadOnlyList<DemoStation> Favorites { get; } =
    [
        new(Make("Nightwave Radio", "pop, hits", "Netherlands"), "nightwave", "", Sound.Music, -9.8, IsAd: true),
        new(Make("Sunrise FM", "pop, 80s, 90s", "United Kingdom"), "sunrise", "Glass Tide - Neon Harbor", Sound.Music, -12.9),
        new(Make("Meridian Talk", "news, talk", "United States"), "meridian", "", Sound.Speech, -16.4, CanZapTo: false),
        new(Make("Velvet Hour", "jazz, soul", "France"), "velvet", "Ida Lane - Small Hours", Sound.Music, -17.1),
        new(Make("Kestrel 88", "rock, alternative", "Germany"), "kestrel", "The Static Hearts - Midnight Drive", Sound.Music, -8.6),
        new(Make("Polar Pop", "pop, dance", "Sweden"), "polar", "", Sound.Unknown, -10.2, IsAssumedAdBreak: true),
        new(Make("Old Oak Classics", "classical", "Austria"), "oldoak", "Aurelia Quartet - Autumn Variations", Sound.Music, -18.3),
        new(Make("Neon Coast", "80s, synthwave", "United States"), "neon", "Luna Ferris - Afterglow Avenue", Sound.Music, -11.4),
        new(Make("Driftwood Radio", "acoustic, folk", "Ireland"), "driftwood", "Wren Hollow - Northern Line", Sound.Music, -13.6),
        new(Make("Lumen FM", "hits, dance", "Belgium"), "lumen", "Juno Park - Lanterns", Sound.Music, -9.1),
    ];

    /// <summary>The station list to search: the favorites and more made-up stations, from all over.</summary>
    public static IReadOnlyList<Station> Catalog { get; } =
    [
        .. Favorites.Select(f => f.Station),
        Make("Amber Coast Radio", "pop, latin", "Portugal"),
        Make("Bluebird Country", "country", "United States"),
        Make("Cedar Hill FM", "adult contemporary", "Canada"),
        Make("Deep Current", "techno, electronic", "Germany"),
        Make("Evergreen Oldies", "oldies, 60s, 70s", "United Kingdom"),
        Make("Fjord FM", "pop", "Norway"),
        Make("Golden Mile Radio", "hits", "Australia"),
        Make("Harbour Jazz", "jazz", "Netherlands"),
        Make("Indigo Lounge", "lounge, chillout", "France"),
        Make("Jukebox 60", "oldies", "United States"),
        Make("Kaleido FM", "pop, hits", "Spain"),
        Make("Little Bay Radio", "local, talk", "Ireland"),
        Make("Moonlit Classics", "classical", "Italy"),
        Make("North Star Rock", "rock, metal", "Finland"),
        Make("Orchard Radio", "indie", "New Zealand"),
        Make("Pebble Beach FM", "chillout", "United Kingdom"),
        Make("Quartz Radio", "electronic", "Switzerland"),
        Make("Riverside Soul", "soul, r&b", "United States"),
        Make("Salt & Pine Radio", "folk", "Canada"),
        Make("Tangerine Radio", "mpb, pop", "Brazil"),
        Make("Urban Pulse", "hip-hop, r&b", "France"),
        Make("Vista 97", "pop, latin", "Mexico"),
        Make("Wildflower FM", "pop", "Denmark"),
        Make("Zenith News", "news", "Germany"),
        Make("Aurora Chill", "ambient", "Iceland"),
        Make("Copper Canyon", "americana", "United States"),
        Make("Starlight Hits", "pop, hits", "Poland"),
        Make("Tidewater Talk", "talk", "United Kingdom"),
    ];

    /// <summary>More songs for the history, next to the ones the favorites play right now.</summary>
    private static readonly string[] EarlierSongs =
    [
        "Marlow - Paper Boats", "Nova Reyes - Gold Rush Summer", "Eli Brandt - Slow Motion City",
        "Coral & Kite - Borrowed Sunlight", "Tomas Vale - Letters from Lisbon", "Saffron Days - Tidal",
        "Milo Arden - Last Train Home", "Pale Orchard - Silver Screen", "The Paper Suns - Weekend Radio",
        "Ada Moreau - Blue Hour Waltz", "Kite Theory - Open Water", "Rosa Lind - Velvet Rain",
        "Hollow Pines - Campfire", "Night Market - Chrome Hearts", "Felix Crane - Satellites",
        "Violet Hours - Sundial", "Echo Park Kids - Summer Static", "Bram Kessel - Cold Coffee",
        "Isla Monroe - Sidewalk Dreams", "Orbit Club - Disco Planet", "Maren Holt - Quiet Storm",
        "The Lamplighters - Morning Glass",
    ];

    public static DemoStation? Find(string url) => Favorites.FirstOrDefault(f => f.Station.Url == url);

    /// <summary>
    /// The minutes of the hour each made-up favorite usually breaks at, by its logo, as its break clock would have
    /// learned them; a station that is left out has not been heard long enough yet.
    /// </summary>
    private static readonly Dictionary<string, int[]> BreakMinutes = new()
    {
        ["nightwave"] = [58, 59, 0, 1, 2, 3, 28, 29, 30],
        ["sunrise"] = [0, 1, 2, 3, 4, 20, 21, 22, 45, 46, 47],
        ["meridian"] = Enumerable.Range(0, 60).Where(m => m % 20 != 9).ToArray(),
        ["velvet"] = [],
        ["kestrel"] = [0, 1, 30, 31, 32],
        ["polar"] = [12, 13, 14, 15, 42, 43, 44],
        ["neon"] = [55, 56, 57, 58],
        ["driftwood"] = [0, 1, 2, 3],
        ["lumen"] = [20, 21, 22, 50, 51, 52],
    };

    /// <summary>
    /// Break clocks for the made-up favorites, made the way the app learns them: some hours of listening, with the
    /// usual break minutes a break in most of them and now and then a break somewhere else.
    /// </summary>
    public static Dictionary<string, BreakClock> BreakClocks()
    {
        var clocks = new Dictionary<string, BreakClock>(StringComparer.Ordinal);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var step = TimeSpan.FromMinutes(1);
        foreach (var favorite in Favorites)
        {
            var clock = clocks[favorite.Station.Url] = new BreakClock();
            var breaks = BreakMinutes.GetValueOrDefault(favorite.Logo);
            var hours = breaks is null ? 3 : 12;
            for (var minute = 0; minute < hours * BreakClock.MinutesPerHour; minute++)
            {
                var at = start.AddMinutes(minute);
                var usual = breaks?.Contains(at.Minute) == true && (minute / 60 + at.Minute) % 4 != 0;
                clock.Record(at, step, usual || (minute * 7 + 3) % 97 == 0);
            }
        }

        return clocks;
    }

    /// <summary>The bundled logo of a made-up station, or null for one without a logo (it then shows its initials).</summary>
    public static string? LogoUrl(Station station) =>
        Find(station.Url) is { } demo
            ? new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Demo", demo.Logo + ".png")).AbsoluteUri
            : null;

    /// <summary>Empties the demo's own data folder and writes the settings and history it starts with, and returns the folder.</summary>
    public static string PrepareDataFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ZapperRadio-demo");
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The files below are written over anyway.
        }

        var now = DateTimeOffset.Now;
        new SettingsStore(Path.Combine(folder, "settings.json")).Save(new AppSettings
        {
            Favorites = Favorites.Select(f => f.Station).ToList(),
            FavoriteTracks =
            [
                new FavoriteTrack("Glass Tide - Neon Harbor", "Sunrise FM", now.AddDays(-2)),
                new FavoriteTrack("Ida Lane - Small Hours", "Velvet Hour", now.AddDays(-5)),
                new FavoriteTrack("Marlow - Paper Boats", "Nightwave Radio", now.AddDays(-9)),
                new FavoriteTrack("Luna Ferris - Afterglow Avenue", "Neon Coast", now.AddDays(-16)),
                new FavoriteTrack("Tomas Vale - Letters from Lisbon", "Driftwood Radio", now.AddDays(-23)),
                new FavoriteTrack("Aurelia Quartet - Autumn Variations", "Old Oak Classics", now.AddDays(-31)),
            ],
            Language = Language,
            ZappOnAdBreaks = true,
            NeverZapTo = Favorites.Where(f => !f.CanZapTo).Select(f => f.Station.Url).ToList(),
            // Off, so the demo does not take the Ctrl+Alt shortcuts from ZapperRadio itself when both run.
            GlobalHotkeys = false,
        });

        new PlayHistoryStore(Path.Combine(folder, "play-history.json")).Save(History(now));
        return folder;
    }

    /// <summary>The songs the favorites played over the last hours, newest first, ending with what they play now.</summary>
    private static List<PlayedTrack> History(DateTimeOffset now)
    {
        var history = new List<PlayedTrack>();
        var playing = Favorites.Where(f => f.Song.Length > 0).ToList();
        for (var i = 0; i < playing.Count; i++)
        {
            history.Add(new PlayedTrack(playing[i].Song, playing[i].Station.Name, playing[i].Station.Url, now.AddMinutes(-1 - (2 * i))));
        }

        var musicStations = Favorites.Where(f => f.Sound == Sound.Music || f.IsAd || f.IsAssumedAdBreak).ToList();
        for (var i = 0; i < 40; i++)
        {
            var station = musicStations[(i * 3) % musicStations.Count].Station;
            history.Add(new PlayedTrack(EarlierSongs[i % EarlierSongs.Length], station.Name, station.Url, now.AddMinutes(-18 - (4 * i))));
        }

        return history;
    }

    /// <summary>A made-up station, at an address that can never be reached.</summary>
    private static Station Make(string name, string tags, string country) =>
        new(name, tags, country, "", $"https://{new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray())}.demo.invalid/stream");
}

/// <summary>A made-up favorite and what it is doing in the demo.</summary>
/// <param name="Logo">The name of its logo in Assets\Demo, without the extension.</param>
/// <param name="Song">The song it plays, or empty when it plays none.</param>
/// <param name="Loudness">Its loudness as measured, in LUFS.</param>
public sealed record DemoStation(
    Station Station,
    string Logo,
    string Song,
    Sound Sound,
    double Loudness,
    bool IsAd = false,
    bool IsAssumedAdBreak = false,
    bool CanZapTo = true)
{
    /// <summary>What the loudness correction does to it, as the app works it out.</summary>
    public double GainDb => Math.Clamp(StationLoudness.Target - Loudness, StationLoudness.MinGainDb, StationLoudness.MaxGainDb);
}
