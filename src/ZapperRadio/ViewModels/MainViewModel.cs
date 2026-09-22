using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using ZapperRadio.Core.Catalog;
using ZapperRadio.Core.Models;
using ZapperRadio.Core.Playback;
using ZapperRadio.Core.Settings;
using ZapperRadio.Core.Shell;
using ZapperRadio.Core.Streaming;
using ZapperRadio.Playback;
using ZapperRadio.Shell;

namespace ZapperRadio.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly HttpClient _http;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly string _cacheFolder;
    private readonly StationDirectory _directory;
    private readonly StationPopularity _popularity;
    private readonly StationHealth _health;
    private readonly StationLogos _logos;

    /// <summary>The stations that failed their last check, by stream URL, with when each last worked.</summary>
    private IReadOnlyDictionary<string, DateTime?> _broken = new Dictionary<string, DateTime?>();
    private readonly Dictionary<string, IReadOnlyDictionary<string, int>> _popularityByCountry = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The key of the worldwide ranking in <see cref="_popularityByCountry"/>, which no country in the list has.</summary>
    private const string WorldwidePopularity = "";
    private readonly HttpClient _streamHttp;
    private readonly IcyProxy _proxy;
    private readonly SoundClassifier? _classifier;
    private readonly RadioEngine _engine;
    private readonly DispatcherQueueTimer _searchDebounce;
    private readonly DispatcherQueueTimer _jumpListTimer;
    private readonly Core.Playback.PlayHistory _history = new();
    private readonly PlayHistoryStore _historyStore;
    private readonly DispatcherQueueTimer _historySaveTimer;
    private readonly DispatcherQueueTimer _historyPruneTimer;
    private readonly DispatcherQueueTimer _historySearchDebounce;
    private readonly DispatcherQueueTimer _heardTimer;

    /// <summary>
    /// How far ahead of what is heard the zapper looks, so a break is cut at its boundary rather than a moment into it.
    /// Losing half a second of a song's fade is better than hearing half a second of an ad.
    /// A zap that fades out has to be over within this lead, so it is longer than <see cref="RadioEngine.CrossfadeLength"/>.
    /// </summary>
    private static readonly TimeSpan ZapLead = TimeSpan.FromMilliseconds(500);

    private List<StationResultViewModel> _allStations = [];

    /// <summary>The result marked as playing, so marking another one does not have to walk the whole list.</summary>
    private StationResultViewModel? _activeResult;
    private CancellationTokenSource? _searchCts;
    private bool _favoritesSyncPending;
    private Station? _lastPlayed;
    private readonly AdBreakZapper _zapper = new();
    private bool _isSwitching;
    private bool _isFirstRun;
    private string? _nowPlayingLogoStationUrl;
    private string? _jumpListShown;
    private Task _jumpListUpdates = Task.CompletedTask;
    private bool _historyChanged;
    private PlayedTrackFilter _historyFilter = new(null);
    private readonly string? _startedWithLanguage;

    public MainViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataFolder = Path.Combine(localAppData, "ZapperRadio");
        MoveOldDataFolder(Path.Combine(localAppData, "WinRadioPlayer"), dataFolder);
        _http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
        // radio-browser.info asks its clients for a user agent with the name and the version of the app.
        var userAgent = $"ZapperRadio/{(AppVersion.Length > 0 ? AppVersion : "0")}";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        var settingsPath = Path.Combine(dataFolder, "settings.json");
        _isFirstRun = !File.Exists(settingsPath);
        _settingsStore = new SettingsStore(settingsPath);
        _settings = _settingsStore.Load();

        // Before anything reads a text, and before the window is built from its XAML: this is where the language is chosen.
        Localizer.Use(_settings.Language);
        AllCountries = Localizer.Get("AllCountries");
        Countries = [AllCountries];
        Languages = [new AppLanguage(null, Localizer.Get("LanguageWindows")), .. Localizer.Languages];
        _startedWithLanguage = (Languages.FirstOrDefault(l => l.Tag == _settings.Language) ?? Languages[0]).Tag;
        TimeShiftOptions = AppSettings.TimeShiftChoices
            .Select(m => new TimeShiftOption(m, Localizer.Format("TimeShiftMinutes", m)))
            .ToList();

        _cacheFolder = Path.Combine(dataFolder, "cache");
        _directory = new StationDirectory(_http, _cacheFolder);
        _popularity = new StationPopularity(_http, _cacheFolder);
        _health = new StationHealth(_http, _cacheFolder);
        _logos = new StationLogos(_http, _cacheFolder);

        // Streams run for hours, so the relay gets a client without the overall request timeout.
        _streamHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _streamHttp.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _proxy = new IcyProxy(_streamHttp);

        _classifier = SoundClassifier.TryCreate(Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "yamnet.onnx"));
        _engine = new RadioEngine(new StreamUrlResolver(_http), _proxy, new TrackDurations(_http), _classifier, dispatcher) { Volume = Math.Clamp(_settings.Volume, 0, 1) };
        _engine.ActiveChanged += (_, _) => UpdateNowPlaying();
        _engine.StreamStatusChanged += (_, stream) => OnStreamStatusChanged(stream);
        _engine.StreamMetadataChanged += (_, stream) => OnStreamMetadataChanged(stream);
        _engine.StreamLoudnessChanged += (_, stream) => OnStreamLoudnessChanged(stream);
        _engine.DelayChanged += (_, _) => OnHeardChanged();

        // Before the first stream is opened, so a station starts at the loudness it was measured at last time,
        // and its buffer is made the length it will keep.
        _engine.NormalizeLoudness = _settings.NormalizeLoudness;
        _engine.TimeShift = TimeSpan.FromMinutes(_settings.ZapToSongStart ? _settings.TimeShiftMinutes : 0);

        // Fires when what is heard of a station played from its buffer changes, which is not when its live stream changes.
        _heardTimer = dispatcher.CreateTimer();
        _heardTimer.IsRepeating = false;
        _heardTimer.Tick += (_, _) => OnHeardChanged();

        foreach (var (url, loudness) in _settings.StationLoudness)
        {
            _engine.SetKnownLoudness(url, loudness);
        }

        _searchDebounce = dispatcher.CreateTimer();
        _searchDebounce.Interval = TimeSpan.FromMilliseconds(250);
        _searchDebounce.IsRepeating = false;
        _searchDebounce.Tick += (_, _) => _ = ApplySearchAsync();

        _jumpListTimer = dispatcher.CreateTimer();
        _jumpListTimer.Interval = TimeSpan.FromSeconds(1);
        _jumpListTimer.IsRepeating = false;
        _jumpListTimer.Tick += (_, _) => UpdateJumpList(withSongs: true);

        _historyStore = new PlayHistoryStore(Path.Combine(dataFolder, "play-history.json"));

        // Every song of every favorite changes the history, which is far too often to write the file for.
        _historySaveTimer = dispatcher.CreateTimer();
        _historySaveTimer.Interval = TimeSpan.FromMinutes(2);
        _historySaveTimer.IsRepeating = false;
        _historySaveTimer.Tick += (_, _) => SaveHistory();

        // Songs also expire while nothing new comes in, for example overnight.
        _historyPruneTimer = dispatcher.CreateTimer();
        _historyPruneTimer.Interval = TimeSpan.FromMinutes(5);
        _historyPruneTimer.Tick += (_, _) => PruneHistory();
        _historyPruneTimer.Start();

        // Filtering redraws the whole list, so it waits for a pause in the typing, like the station search does.
        _historySearchDebounce = dispatcher.CreateTimer();
        _historySearchDebounce.Interval = TimeSpan.FromMilliseconds(250);
        _historySearchDebounce.IsRepeating = false;
        _historySearchDebounce.Tick += (_, _) => ApplyHistorySearch();

        Volume = _engine.Volume * 100;
        SelectedCountry = _settings.Country ?? AllCountries;

        foreach (var station in _settings.Favorites.DistinctBy(s => s.Url).Take(AppSettings.MaxFavorites))
        {
            AddFavorite(station);
        }

        foreach (var track in _settings.FavoriteTracks)
        {
            FavoriteTracks.Add(track);
        }

        // After the favorite tracks, so each song in the history knows whether it is one of them.
        _history.Load(_historyStore.Load(), DateTimeOffset.Now);
        foreach (var track in _history.Entries)
        {
            PlayHistory.Add(new PlayedTrackViewModel(track) { IsSaved = FavoriteTracks.Any(t => t.IsSameSong(track.Title)) });
        }

        ApplyHistorySearch();

        FavoriteTracks.CollectionChanged += OnFavoriteTracksChanged;
        Favorites.CollectionChanged += OnFavoritesChanged;
        SyncFavorites();
        // After the favorites are loaded, because changing these saves the settings.
        ZappOnAdBreaks = _settings.ZappOnAdBreaks;
        ZapBackAfterBreak = _settings.ZapBackAfterBreak;
        CrossfadeZaps = _settings.CrossfadeZaps;
        UpdateNeverZapTo();
        NormalizeLoudness = _settings.NormalizeLoudness;
        // The switch first: the length only applies to the engine once it is chosen, so it is applied once, as set.
        ZapToSongStart = _settings.ZapToSongStart;
        SelectedTimeShift = TimeShiftOptions.FirstOrDefault(o => o.Minutes == _settings.TimeShiftMinutes) ?? TimeShiftOptions[1];
        GlobalHotkeys = _settings.GlobalHotkeys;
        IsCompact = _settings.IsCompact;
        SelectedLanguage = Languages.FirstOrDefault(l => l.Tag == _settings.Language) ?? Languages[0];
        // Read from the registry rather than settings.json: it also picks up a change made from
        // Windows' own Startup Apps settings instead of from here.
        AutoStart = StartupRegistration.IsEnabled();

        StartUpdateChecks();
    }

    /// <summary>
    /// The app was called WinRadioPlayer before; move its settings and cache so favorites survive the rename.
    /// </summary>
    private static void MoveOldDataFolder(string oldFolder, string newFolder)
    {
        try
        {
            if (Directory.Exists(oldFolder) && !Directory.Exists(newFolder))
            {
                Directory.Move(oldFolder, newFolder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Starting with fresh settings is better than not starting at all.
        }
    }

    /// <summary>
    /// What the country box shows for "no country filter", and what it is compared with. Only stored in the
    /// settings as null, never as this text, so the language can change between runs without breaking the choice.
    /// </summary>
    public string AllCountries { get; }

    /// <summary>The languages to choose from in the settings: the language of Windows first, then each one named in itself.</summary>
    public IReadOnlyList<AppLanguage> Languages { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsRestartForLanguage))]
    public partial AppLanguage SelectedLanguage { get; set; }

    /// <summary>Whether the chosen language is not the one the app is showing, which it only does after it is started again.</summary>
    public bool NeedsRestartForLanguage => SelectedLanguage.Tag != _startedWithLanguage;

    partial void OnSelectedLanguageChanged(AppLanguage value)
    {
        _settings.Language = value.Tag;
        SaveSettings();
    }

    public ObservableCollection<FavoriteViewModel> Favorites { get; } = [];

    public string FavoritesHeader => Localizer.Format("FavoritesHeader", Favorites.Count, AppSettings.MaxFavorites);

    public bool HasNoFavorites => Favorites.Count == 0;

    /// <summary>Songs saved with the heart while listening, newest first.</summary>
    public ObservableCollection<FavoriteTrack> FavoriteTracks { get; } = [];

    public string FavoriteTracksHeader => Localizer.Format("FavoriteTracksHeader", FavoriteTracks.Count);

    public bool HasNoFavoriteTracks => FavoriteTracks.Count == 0;

    /// <summary>Everything the favorites played over the last twelve hours, newest first.</summary>
    public ObservableCollection<PlayedTrackViewModel> PlayHistory { get; } = [];

    /// <summary>
    /// The part of <see cref="PlayHistory"/> the list shows: the songs matching <see cref="HistorySearchText"/>,
    /// or all of them while the search box is empty.
    /// </summary>
    public ObservableCollection<PlayedTrackViewModel> PlayHistoryResults { get; } = [];

    /// <summary>What to search the history for: words of a song, of an artist or of a station name.</summary>
    [ObservableProperty]
    public partial string HistorySearchText { get; set; } = "";

    public bool HasNoPlayHistory => PlayHistory.Count == 0;

    /// <summary>Whether there is anything to search, which is what the search box waits for.</summary>
    public bool HasPlayHistory => PlayHistory.Count > 0;

    /// <summary>Whether songs were played but the search hides every one of them.</summary>
    public bool HasNoHistoryMatches => PlayHistory.Count > 0 && PlayHistoryResults.Count == 0;

    /// <summary>Which of the tabs is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingStations))]
    [NotifyPropertyChangedFor(nameof(IsShowingZapper))]
    [NotifyPropertyChangedFor(nameof(IsShowingFavoriteTracks))]
    [NotifyPropertyChangedFor(nameof(IsShowingPlayHistory))]
    public partial MainTab SelectedTab { get; set; }

    public bool IsShowingStations => SelectedTab == MainTab.Stations;

    public bool IsShowingZapper => SelectedTab == MainTab.Zapper;

    partial void OnSelectedTabChanged(MainTab value)
    {
        if (value == MainTab.Zapper)
        {
            RefreshTimeShiftMemory();
        }
    }

    public bool IsShowingFavoriteTracks => SelectedTab == MainTab.FavoriteTracks;

    public bool IsShowingPlayHistory => SelectedTab == MainTab.PlayHistory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsSummary))]
    [NotifyPropertyChangedFor(nameof(HasNoResults))]
    [NotifyPropertyChangedFor(nameof(NoResultsText))]
    [NotifyPropertyChangedFor(nameof(CanSearchAllCountries))]
    public partial IReadOnlyList<StationResultViewModel> Results { get; set; } = [];

    /// <summary>How many stations the search shows, or nothing until the list is there to search.</summary>
    public string ResultsSummary => _allStations.Count == 0 ? ""
        : Results.Count == 1 ? Localizer.Get("ResultsOne")
        : Localizer.Format("ResultsMany", Results.Count);

    /// <summary>Whether the list is loaded but the search hides every station in it.</summary>
    public bool HasNoResults => _allStations.Count > 0 && Results.Count == 0;

    public string NoResultsText => SelectedCountry == AllCountries
        ? Localizer.Get("NoResultsText")
        : Localizer.Format("NoResultsInCountry", SelectedCountry);

    /// <summary>Whether a country hides what the search found, so searching every country could find it.</summary>
    public bool CanSearchAllCountries => HasNoResults && SelectedCountry != AllCountries;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> Countries { get; set; } = [];

    [ObservableProperty]
    public partial string SelectedCountry { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string CatalogStatus { get; set; } = "";

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsErrorOpen { get; set; }

    public StationLogoViewModel NowPlayingLogo { get; } = new();

    /// <summary>Where the logo of the station being listened to is, for the Windows media card to load as its thumbnail.</summary>
    [ObservableProperty]
    public partial string? NowPlayingLogoUrl { get; set; }

    [ObservableProperty]
    public partial string NowPlayingName { get; set; } = "";

    [ObservableProperty]
    public partial string NowPlayingStatus { get; set; } = "";

    /// <summary>The current song of the station being listened to, or empty when unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNowPlayingSong))]
    public partial string NowPlayingSong { get; set; } = "";

    public bool HasNowPlayingSong => NowPlayingSong.Length > 0;

    /// <summary>The song the heart saves: the one shown as playing, or null during an ad break.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveNowPlayingTrack))]
    public partial string? NowPlayingTrack { get; set; }

    public bool CanSaveNowPlayingTrack => NowPlayingTrack is not null;

    [ObservableProperty]
    public partial bool IsNowPlayingTrackSaved { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial double Volume { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    /// <summary>Zap to another favorite during the ad breaks of the station being listened to, and back once they are over.</summary>
    [ObservableProperty]
    public partial bool ZappOnAdBreaks { get; set; }

    /// <summary>Whether the zapper goes back to the station it zapped away from once its break is over, or stays where it landed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StayAfterBreak))]
    public partial bool ZapBackAfterBreak { get; set; } = true;

    /// <summary>The other choice of <see cref="ZapBackAfterBreak"/>, for the second of the two radio buttons.</summary>
    public bool StayAfterBreak
    {
        get => !ZapBackAfterBreak;
        set => ZapBackAfterBreak = !value;
    }

    /// <summary>Whether a zap fades from one station into the other instead of cutting over.</summary>
    [ObservableProperty]
    public partial bool CrossfadeZaps { get; set; } = true;

    /// <summary>Whether the loud stations are turned down to the level of the rest, so zapping keeps one volume.</summary>
    [ObservableProperty]
    public partial bool NormalizeLoudness { get; set; } = true;

    /// <summary>The lengths the time-shift buffer can be set to, as the settings name them.</summary>
    public IReadOnlyList<TimeShiftOption> TimeShiftOptions { get; }

    /// <summary>Whether a zap starts the song on the other station from its beginning, which keeps a buffer of every favorite.</summary>
    [ObservableProperty]
    public partial bool ZapToSongStart { get; set; } = true;

    /// <summary>How much of every favorite is kept, so a zap can start the song on the other station from its beginning.</summary>
    [ObservableProperty]
    public partial TimeShiftOption? SelectedTimeShift { get; set; }

    /// <summary>What the buffers take in memory for the favorites, or would take when switched on, as the Zapper tab shows it.</summary>
    [ObservableProperty]
    public partial string TimeShiftMemory { get; set; } = "";

    /// <summary>Whether the station being listened to is played from its buffer, behind the broadcast.</summary>
    [ObservableProperty]
    public partial bool IsTimeShifted { get; set; }

    /// <summary>Whether the Ctrl+Alt shortcuts also work while another app has focus.</summary>
    [ObservableProperty]
    public partial bool GlobalHotkeys { get; set; }

    /// <summary>Whether ZapperRadio launches when the user signs in to Windows.</summary>
    [ObservableProperty]
    public partial bool AutoStart { get; set; }

    /// <summary>Which of the global shortcuts another app already holds, or empty when they all work.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGlobalHotkeyProblem))]
    public partial string GlobalHotkeyStatus { get; set; } = "";

    public bool HasGlobalHotkeyProblem => GlobalHotkeyStatus.Length > 0;

    /// <summary>
    /// Whether the compact window is shown: only the favorites, to click and listen. Searching and adding favorites
    /// is done in the full window.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFullView))]
    public partial bool IsCompact { get; set; }

    public bool IsFullView => !IsCompact;

    partial void OnIsCompactChanged(bool value)
    {
        _settings.IsCompact = value;
        SaveSettings();
    }

    [RelayCommand]
    private void ToggleCompact() => IsCompact = !IsCompact;

    /// <summary>Where the compact or the full window was last left, or null when it has not been used yet.</summary>
    public WindowPlacement? WindowPlacement(bool compact) => compact ? _settings.CompactWindow : _settings.FullWindow;

    /// <summary>Remembers where a window is; kept in memory until <see cref="Save"/>, as it changes with every drag.</summary>
    public void RememberWindow(bool compact, WindowPlacement placement)
    {
        if (compact)
        {
            _settings.CompactWindow = placement;
        }
        else
        {
            _settings.FullWindow = placement;
        }
    }

    /// <summary>Writes the settings out, e.g. once the window has been moved or resized.</summary>
    public void Save() => SaveSettings();

    /// <summary>The version of the app, as shown in the settings.</summary>
    public string AppVersion { get; } =
        typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)
        ?? "";

    /// <summary>The file the station list was read from, e.g. <c>stations-2026-09-17.rsd</c>.</summary>
    [ObservableProperty]
    public partial string StationListName { get; set; } = "";

    /// <summary>When the station list was downloaded, which is not the day it was generated.</summary>
    [ObservableProperty]
    public partial string StationListUpdated { get; set; } = "";

    /// <summary>How much the logo and popularity lookups take up on disk.</summary>
    [ObservableProperty]
    public partial string CacheSummary { get; set; } = "";

    /// <summary>Counts what the cache holds; called each time the settings are opened.</summary>
    public void RefreshCacheSummary()
    {
        try
        {
            var files = CachedLookups().Select(file => new FileInfo(file).Length).ToList();
            var bytes = files.Sum();
            CacheSummary = files.Count == 0
                ? Localizer.Get("CacheEmpty")
                : Localizer.Format("CacheSummary", files.Count, bytes < 1024 * 1024 ? $"{bytes / 1024.0:N0} KB" : $"{bytes / (1024.0 * 1024):N1} MB");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CacheSummary = "";
        }
    }

    private IEnumerable<string> CachedLookups() =>
        Directory.Exists(_cacheFolder)
            ? Directory.EnumerateFiles(_cacheFolder, "logo-*.txt")
                .Concat(Directory.EnumerateFiles(_cacheFolder, "popularity-*.txt"))
                .Concat(Directory.EnumerateFiles(_cacheFolder, StationHealth.CacheFileName))
            : [];

    /// <summary>
    /// Throws away the logos and the most-played lists, and looks them up again. The station list itself is kept:
    /// it is the one thing in the cache that also works offline, and it has its own button to check for a new one.
    /// </summary>
    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        _logos.Clear();
        try
        {
            foreach (var file in CachedLookups().ToList())
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(Localizer.Format("CacheClearFailed", ex.Message));
        }

        _popularityByCountry.Clear();
        _ = LoadHealthAsync();
        RefreshCacheSummary();

        foreach (var favorite in Favorites)
        {
            favorite.Logo.Reset(favorite.Name);
            _ = LoadFavoriteLogoAsync(favorite);
        }

        // The logo of the station being listened to is loaded for the station, not for a favorite.
        _nowPlayingLogoStationUrl = null;
        UpdateNowPlaying();
        await ApplySearchAsync();
    }

    public async Task LoadCatalogAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        _popularityByCountry.Clear();
        _ = LoadHealthAsync();
        CatalogStatus = Localizer.Get("CatalogLoading");
        try
        {
            var catalog = await _directory.LoadAsync();

            var stations = await Task.Run(() =>
            {
                var all = catalog.Stations
                    .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(s => new StationResultViewModel(s))
                    .ToList();
                var countries = catalog.Stations
                    .Select(s => s.Country)
                    .Where(c => c.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.CurrentCultureIgnoreCase)
                    .Prepend(AllCountries)
                    .ToList();
                return (all, countries);
            });

            _allStations = stations.all;
            RefreshFavoriteMarks();
            RefreshHealthMarks();
            // The results are new objects, so the mark on the old ones is dropped along with them.
            _activeResult = null;
            MarkActiveResult(_engine.Active?.Station);

            var country = SelectedCountry;
            if (_isFirstRun && country == AllCountries && WindowsRegion.GetIsoCode() is { } region)
            {
                // Without saved settings, start with the country set in Windows.
                country = StationPopularity.FindCountry(stations.countries, region) ?? country;
            }

            _isFirstRun = false;
            Countries = stations.countries;
            SelectedCountry = stations.countries.Contains(country) ? country : AllCountries;
            // Refresh the country box, which may still show text typed while the list was loading.
            OnPropertyChanged(nameof(SelectedCountry));

            var date = catalog.GeneratedAt?.ToString("d") ?? catalog.FileName;
            CatalogStatus = Localizer.Format(catalog.FromCache ? "CatalogStatusOffline" : "CatalogStatus", _allStations.Count, date);
            UpdateStationListInfo(catalog.FileName);
            await ApplySearchAsync();
        }
        catch (Exception ex)
        {
            CatalogStatus = Localizer.Get("CatalogUnavailable");
            // The directory wraps a failed download in its own English sentence; the cause inside it is what to add to the local one.
            ShowError(ex is StationDirectoryException { InnerException: { } cause }
                ? Localizer.Format("CatalogDownloadFailed", StationDirectory.DefaultIndexUri, cause.Message)
                : ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task ReloadCatalog() => LoadCatalogAsync();

    private void UpdateStationListInfo(string fileName)
    {
        StationListName = fileName;
        try
        {
            var path = Path.Combine(_cacheFolder, fileName);
            StationListUpdated = File.Exists(path)
                ? Localizer.Format("StationListDownloaded", File.GetLastWriteTime(path))
                : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StationListUpdated = "";
        }
    }

    public void Play(Station station)
    {
        _lastPlayed = station;
        // A station picked by hand, with a click, a shortcut, a media key or the jump list, plays live: you chose what
        // is on right now. Only the zapper starts a station at the beginning of its song.
        Switch(station, null);
        if (_engine.Active is { } active)
        {
            // Picking a station ends any zapping, and picking it during its ad break means you want to hear it anyway.
            _zapper.OnPicked(active.Station.Url, HeardChannelOf(active));
        }
    }

    /// <summary>Plays the station being listened to live again, leaving what the buffer still holds of it.</summary>
    [RelayCommand]
    private void GoLive() => _engine.GoLive();

    /// <summary>
    /// Where to start a station so it is heard from the beginning of the song it plays, or null to play it live: when
    /// it plays no song, or when it cannot be told where the song began. The timeline dates the song back to the same
    /// moment, so from where it lands the station is heard as playing the song.
    /// </summary>
    private static DateTimeOffset? SongStartOf(StationStream stream) =>
        ChannelOf(stream) == ChannelState.Song && stream.SongStartedAt is { } began ? began - StreamTimeline.SongPreRoll : null;

    /// <summary>
    /// Moves to the next favorite, wrapping around at the end of the list: the next-track button of the media
    /// card and of the media keys, which is zapping by hand.
    /// </summary>
    [RelayCommand]
    private void PlayNextFavorite() => StepFavorite(1);

    /// <summary>Moves to the previous favorite, wrapping around at the start of the list.</summary>
    [RelayCommand]
    private void PlayPreviousFavorite() => StepFavorite(-1);

    private void StepFavorite(int step)
    {
        var urls = Favorites.Select(f => f.Station.Url).ToList();
        // While zapping is on, next and previous pass over the favorites in an ad break or talking, which is what
        // the zapper would leave anyway.
        if (FavoriteRing.Step(urls, _engine.Active?.Station.Url, step, IsInBreak) is { } url
            && Favorites.FirstOrDefault(f => f.Station.Url == url) is { } favorite)
        {
            Play(favorite.Station);
        }
    }

    /// <summary>Whether a favorite is in a break the zapper leaves; always false while zapping is off.</summary>
    private bool IsInBreak(string url) =>
        ZappOnAdBreaks && _engine.Find(url) is { } stream && ChannelOf(stream) is ChannelState.Ad or ChannelState.Speech;

    [RelayCommand]
    private void TogglePlayback()
    {
        if (_engine.Active is not null)
        {
            _engine.Stop();
            _zapper.OnStopped();
        }
        else if ((_lastPlayed ?? Favorites.FirstOrDefault()?.Station) is { } station)
        {
            Play(station);
        }
    }

    public void ToggleFavorite(Station station)
    {
        var existing = Favorites.FirstOrDefault(f => f.Station.Url == station.Url);
        if (existing is not null)
        {
            Favorites.Remove(existing);
            return;
        }

        if (Favorites.Count >= AppSettings.MaxFavorites)
        {
            ShowError(Localizer.Format("FavoritesLimitReached", AppSettings.MaxFavorites));
            return;
        }

        AddFavorite(station);
    }

    private void AddFavorite(Station station)
    {
        var favorite = new FavoriteViewModel(station) { CanZapTo = !_settings.NeverZapTo.Contains(station.Url) };
        favorite.CanZapToChanged += OnCanZapToChanged;
        Favorites.Add(favorite);
        _ = LoadFavoriteLogoAsync(favorite);
    }

    public void RemoveFavorite(FavoriteViewModel favorite) => Favorites.Remove(favorite);

    private async Task LoadFavoriteLogoAsync(FavoriteViewModel favorite)
    {
        var url = await _logos.GetLogoUrlAsync(favorite.Station);
        // The favorite might have been removed again while the lookup was running.
        if (Favorites.Contains(favorite))
        {
            favorite.Logo.SetLogoUrl(url);
        }
    }

    /// <summary>Saves the song playing right now to the favorite tracks, or removes it when it is already there.</summary>
    [RelayCommand]
    private void ToggleNowPlayingTrackSaved()
    {
        if (NowPlayingTrack is not { } title || _engine.Active is not { } active)
        {
            return;
        }

        if (FavoriteTracks.FirstOrDefault(t => t.IsSameSong(title)) is { } saved)
        {
            FavoriteTracks.Remove(saved);
        }
        else
        {
            FavoriteTracks.Insert(0, new FavoriteTrack(title, active.Station.Name, DateTimeOffset.Now));
        }
    }

    public void RemoveFavoriteTrack(FavoriteTrack track) => FavoriteTracks.Remove(track);

    /// <summary>Saves a song from the play history to the favorite tracks, or removes it when it is already there.</summary>
    public void ToggleHistoryTrackSaved(PlayedTrackViewModel entry)
    {
        if (FavoriteTracks.FirstOrDefault(t => t.IsSameSong(entry.Title)) is { } saved)
        {
            FavoriteTracks.Remove(saved);
        }
        else
        {
            FavoriteTracks.Insert(0, new FavoriteTrack(entry.Title, entry.Track.StationName, entry.Track.PlayedAt));
        }
    }

    /// <summary>Empties the play history, for when you would rather not keep what was played.</summary>
    [RelayCommand]
    private void ClearPlayHistory()
    {
        _history.Clear();
        PlayHistory.Clear();
        // A search over an emptied history would only hide the line explaining that it is empty.
        HistorySearchText = "";
        ApplyHistorySearch();
        // Throwing it away is deliberate, so it does not sit in the file until the next save.
        _historyChanged = true;
        SaveHistory();
    }

    /// <summary>
    /// Notes the song a station just started. Every stream reports its titles, listened to or not, so the
    /// history covers all favorites. Ad markers and the repeated title after a reconnect are left out.
    /// </summary>
    private void RecordPlayed(StationStream stream)
    {
        if (stream.Metadata is not { IsAd: false, Title: { } title })
        {
            return;
        }

        if (_history.Add(title, stream.Station.Name, stream.Station.Url, DateTimeOffset.Now) is { } track)
        {
            var entry = new PlayedTrackViewModel(track) { IsSaved = FavoriteTracks.Any(t => t.IsSameSong(track.Title)) };
            PlayHistory.Insert(0, entry);
            if (_historyFilter.Matches(track))
            {
                PlayHistoryResults.Insert(0, entry);
            }

            TrimHistoryToModel();
            SaveHistoryLater();
            OnPlayHistoryChanged();
        }
    }

    private void PruneHistory()
    {
        var before = _history.Count;
        _history.Prune(DateTimeOffset.Now);
        if (_history.Count != before)
        {
            TrimHistoryToModel();
            SaveHistoryLater();
            OnPlayHistoryChanged();
        }
    }

    private void SaveHistoryLater()
    {
        _historyChanged = true;
        if (!_historySaveTimer.IsRunning)
        {
            _historySaveTimer.Start();
        }
    }

    /// <summary>The history only ever loses its oldest songs, so the shown rows follow it from the end.</summary>
    private void TrimHistoryToModel()
    {
        while (PlayHistory.Count > _history.Count)
        {
            var dropped = PlayHistory[^1];
            PlayHistory.RemoveAt(PlayHistory.Count - 1);
            PlayHistoryResults.Remove(dropped);
        }
    }

    partial void OnHistorySearchTextChanged(string value)
    {
        _historySearchDebounce.Stop();
        _historySearchDebounce.Start();
    }

    /// <summary>Puts the songs matching the search in the shown list, newest first like the history itself.</summary>
    private void ApplyHistorySearch()
    {
        _historySearchDebounce.Stop();
        _historyFilter = new PlayedTrackFilter(HistorySearchText);

        PlayHistoryResults.Clear();
        foreach (var entry in PlayHistory)
        {
            if (_historyFilter.Matches(entry.Track))
            {
                PlayHistoryResults.Add(entry);
            }
        }

        OnPlayHistoryChanged();
    }

    private void OnPlayHistoryChanged()
    {
        OnPropertyChanged(nameof(HasNoPlayHistory));
        OnPropertyChanged(nameof(HasPlayHistory));
        OnPropertyChanged(nameof(HasNoHistoryMatches));
    }

    private void SaveHistory()
    {
        _historySaveTimer.Stop();
        if (!_historyChanged)
        {
            return;
        }

        _historyChanged = false;
        try
        {
            _historyStore.Save(_history.Entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The history is a convenience; failing to keep it is not worth interrupting for.
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    [RelayCommand]
    private void SearchAllCountries() => SelectedCountry = AllCountries;

    /// <summary>
    /// Plays the best match of the search, so a name and Enter is enough. A search still waiting for the pause in
    /// the typing is run first, or Enter would play the best match of what was typed before.
    /// </summary>
    public async Task PlayBestMatchAsync()
    {
        if (_searchDebounce.IsRunning)
        {
            _searchDebounce.Stop();
            await ApplySearchAsync();
        }

        if (Results.FirstOrDefault() is { } best)
        {
            Play(best.Station);
        }
    }

    /// <summary>
    /// The countries containing <paramref name="text"/>, those starting with it first.
    /// Empty text matches every country, including <see cref="AllCountries"/>.
    /// </summary>
    public IReadOnlyList<string> MatchCountries(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return Countries;
        }

        return Countries
            .Where(c => c.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(c => !c.StartsWith(text, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    partial void OnSelectedCountryChanged(string value)
    {
        if (value is null)
        {
            return;
        }

        _settings.Country = value == AllCountries ? null : value;
        _ = ApplySearchAsync();
    }

    partial void OnVolumeChanged(double value)
    {
        _engine.Volume = Math.Clamp(value / 100, 0, 1);
        // Turning the volume up is a clear sign you want to hear something.
        if (value > 0)
        {
            IsMuted = false;
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        _engine.IsMuted = value;
        UpdateNowPlaying();
        ScheduleJumpListUpdate();
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    partial void OnGlobalHotkeysChanged(bool value)
    {
        _settings.GlobalHotkeys = value;
        SaveSettings();
    }

    partial void OnAutoStartChanged(bool value) => StartupRegistration.SetEnabled(value);

    partial void OnZapBackAfterBreakChanged(bool value)
    {
        _settings.ZapBackAfterBreak = value;
        _zapper.ReturnAfterBreak = value;
        SaveSettings();
    }

    partial void OnCrossfadeZapsChanged(bool value)
    {
        _settings.CrossfadeZaps = value;
        SaveSettings();
    }

    /// <summary>A favorite was switched to be zapped to or not.</summary>
    private void OnCanZapToChanged(object? sender, EventArgs e)
    {
        UpdateNeverZapTo();
        SaveSettings();
    }

    private void UpdateNeverZapTo() =>
        _zapper.NeverZapTo = Favorites.Where(f => !f.CanZapTo).Select(f => f.Station.Url).ToHashSet(StringComparer.Ordinal);

    partial void OnNormalizeLoudnessChanged(bool value)
    {
        _settings.NormalizeLoudness = value;
        _engine.NormalizeLoudness = value;
        UpdateLoudnessTexts();
        SaveSettings();
    }

    partial void OnZapToSongStartChanged(bool value) => ApplyTimeShift();

    partial void OnSelectedTimeShiftChanged(TimeShiftOption? value) => ApplyTimeShift();

    /// <summary>Keeps the buffers the switch and the length ask for; switched off, the memory they took is freed.</summary>
    private void ApplyTimeShift()
    {
        if (SelectedTimeShift is not { } length)
        {
            return;
        }

        _settings.ZapToSongStart = ZapToSongStart;
        _settings.TimeShiftMinutes = length.Minutes;
        _engine.TimeShift = TimeSpan.FromMinutes(ZapToSongStart ? length.Minutes : 0);
        RefreshTimeShiftMemory();
        SaveSettings();
    }

    /// <summary>
    /// Works out what the buffers take for the favorites, or would take when switched on. Called whenever the Zapper
    /// tab is shown and the favorites change, because the bitrates are only known once the stations have answered.
    /// </summary>
    public void RefreshTimeShiftMemory()
    {
        var length = TimeSpan.FromMinutes(SelectedTimeShift?.Minutes ?? 5);
        if (Favorites.Count == 0)
        {
            // Nothing to add up yet, so what one favorite takes at the most common bitrate.
            var perFavorite = TimeShiftBuffer.CapacityFor(length, 128) / (1024.0 * 1024);
            TimeShiftMemory = Localizer.Format("TimeShiftMemoryPerFavorite", Math.Round(perFavorite, 1));
            return;
        }

        var megabytes = Math.Max(1, Math.Round(_engine.BufferBytes(length) / (1024.0 * 1024)));
        TimeShiftMemory = Localizer.Format(ZapToSongStart ? "TimeShiftMemory" : "TimeShiftMemoryOff", megabytes, Favorites.Count);
    }

    /// <summary>The loudness button of the settings: every favorite measures its music again and corrects itself to the result.</summary>
    [RelayCommand]
    private void RemeasureLoudness() => _engine.RemeasureLoudness();

    private void OnStreamLoudnessChanged(StationStream stream)
    {
        if (stream.MeasuredLoudness is { } loudness)
        {
            _settings.StationLoudness[stream.Station.Url] = loudness;
            SaveSettings();
        }

        UpdateLoudnessTexts();
    }

    /// <summary>Refreshes what the loudness section of the settings says about each favorite.</summary>
    private void UpdateLoudnessTexts()
    {
        foreach (var favorite in Favorites)
        {
            favorite.LoudnessText = _engine.Find(favorite.Station.Url) is { } stream
                ? LoudnessTexts.For(stream.MeasuredLoudness, stream.MeasuredGainDb, NormalizeLoudness, stream.IsRemeasuring)
                : LoudnessTexts.NotMeasured;
        }
    }

    partial void OnZappOnAdBreaksChanged(bool value)
    {
        _settings.ZappOnAdBreaks = value;
        SaveSettings();
        if (!value)
        {
            // Don't jump back to a station later on, after zapping was turned off.
            _zapper.OnStopped();
        }

        ZapOnAdBreak();
    }

    /// <summary>
    /// Zaps away from an ad break or speech, or back to the station after its break. Runs on every change of any stream,
    /// so it also zaps once another favorite becomes available, when none was at the start of the break.
    /// </summary>
    private void ZapOnAdBreak()
    {
        if (!ZappOnAdBreaks || _engine.Active is not { } active)
        {
            return;
        }

        // The other favorites are judged live, because a zap to one of them starts at the beginning of the song it
        // plays now. The station being listened to is judged by what is heard of it, which is behind the broadcast
        // when it is played from its buffer.
        var favorites = Favorites
            .Select(f => _engine.Find(f.Station.Url))
            .OfType<StationStream>()
            .Select(s => new Channel(s.Station.Url, ChannelOf(s)))
            .ToList();
        if (_zapper.Next(new Channel(active.Station.Url, HeardChannelOf(active)), favorites, DateTimeOffset.UtcNow) is { } url
            && _engine.Find(url) is { } next)
        {
            _lastPlayed = next.Station;
            Switch(next.Station, SongStartOf(next), CrossfadeZaps);
        }
    }

    /// <summary>
    /// Switches the engine to a station. The switch itself reports that what is heard changed, and the zapper must not
    /// act on that halfway: a station picked by hand in its break would be zapped away from before the pick counts.
    /// </summary>
    private void Switch(Station station, DateTimeOffset? from, bool crossfade = false)
    {
        _isSwitching = true;
        try
        {
            _engine.Play(station, from, crossfade);
        }
        finally
        {
            _isSwitching = false;
        }
    }

    private static ChannelState ChannelOf(StationStream stream) =>
        Channel.StateOf(stream.IsInAdBreak, stream.Status == StreamStatus.Live, stream.Metadata?.IsSong == true, stream.Sound);

    /// <summary>
    /// What the zapper makes of the station being listened to: what is about to be heard of it, played from the buffer,
    /// or its live state otherwise.
    /// </summary>
    private ChannelState HeardChannelOf(StationStream stream) =>
        _engine.IsTimeShifted && stream == _engine.Active ? stream.MomentAt(_engine.HeardAt + ZapLead).State : ChannelOf(stream);

    /// <summary>
    /// What is heard of the station being listened to moved on: the replay reached another moment of its timeline, or
    /// the delay changed. Updates what the window shows, lets the zapper act on it, and waits for the next moment.
    /// </summary>
    private void OnHeardChanged()
    {
        IsTimeShifted = _engine.IsTimeShifted;
        if (_engine.Active is { } active)
        {
            foreach (var favorite in Favorites.Where(f => f.Station.Url == active.Station.Url))
            {
                ShowOnFavorite(favorite, active);
            }
        }

        UpdateNowPlaying();
        if (!_isSwitching)
        {
            ZapOnAdBreak();
        }

        ScheduleHeardChange();
    }

    /// <summary>Sets the timer for when what is heard of the station being listened to next changes.</summary>
    private void ScheduleHeardChange()
    {
        _heardTimer.Stop();
        if (!_engine.IsTimeShifted || _engine.Active is not { } active)
        {
            return;
        }

        var ahead = _engine.HeardAt + ZapLead;
        if (active.NextMomentAfter(ahead) is { } next)
        {
            var wait = next - ahead;
            _heardTimer.Interval = wait > TimeSpan.FromMilliseconds(50) ? wait : TimeSpan.FromMilliseconds(50);
            _heardTimer.Start();
        }
    }

    /// <summary>What a favorite's row shows: what is heard of it, which for the station being listened to may be behind the broadcast.</summary>
    private void ShowOnFavorite(FavoriteViewModel favorite, StationStream stream)
    {
        var moment = _engine.HeardOf(stream);
        favorite.Sound = moment.Sound;
        favorite.IsAd = moment.Metadata?.IsAd == true;
        favorite.IsAssumedAdBreak = moment is { Metadata.IsAd: not true, IsAssumedAdBreak: true };
        var song = SongTexts.For(moment);
        if (favorite.Song != song)
        {
            favorite.Song = song;
            ScheduleJumpListUpdate();
        }
    }

    private async Task ApplySearchAsync()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        var country = SelectedCountry == AllCountries ? null : SelectedCountry;
        var filter = new StationFilter(SearchText, country);
        var source = _allStations;

        // Without a country the worldwide ranking is used, so the list does not open on whatever sorts first by name.
        if (!_popularityByCountry.TryGetValue(country ?? WorldwidePopularity, out var ranks))
        {
            _ = LoadPopularityAsync(country);
        }

        try
        {
            var results = await Task.Run(() =>
            {
                // The best matches come first, and the most popular among equally good ones; the sorts are stable,
                // so the stations that are neither keep name order.
                // A station that is down sinks below the working ones that match as well, but stays in the list: a
                // failed check can be an hour's outage, and a name typed in full still finds it at the top.
                var matches = source
                    .Select(s => (Item: s, Relevance: filter.Relevance(s.Station)))
                    .Where(m => m.Relevance is not null)
                    .OrderBy(m => m.Relevance)
                    .ThenBy(m => m.Item.IsOffline);
                if (ranks is { Count: > 0 })
                {
                    matches = matches.ThenBy(m => ranks.TryGetValue(m.Item.Station.Url, out var rank) ? rank : int.MaxValue);
                }

                return matches.Select(m => m.Item).ToList();
            }, cts.Token);

            if (!cts.IsCancellationRequested)
            {
                Results = results;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Finds out which stations are down, and moves them down the results once it is known.</summary>
    private async Task LoadHealthAsync()
    {
        try
        {
            _broken = await _health.GetBrokenAsync();
        }
        catch (Exception)
        {
            // Like the popularity, a nice-to-have: without it every station counts as working.
            return;
        }

        RefreshHealthMarks();
        RefreshCacheSummary();
        await ApplySearchAsync();
    }

    private void RefreshHealthMarks()
    {
        foreach (var result in _allStations)
        {
            var offline = _broken.TryGetValue(result.Station.Url, out var lastWorked);
            if (offline || result.IsOffline)
            {
                result.MarkOffline(offline, lastWorked);
            }
        }
    }

    /// <param name="country">The country to rank, or null for the whole world.</param>
    private async Task LoadPopularityAsync(string? country)
    {
        var key = country ?? WorldwidePopularity;
        // Mark as loading so repeated searches don't start the same request again.
        _popularityByCountry[key] = new Dictionary<string, int>();
        try
        {
            _popularityByCountry[key] = await _popularity.GetRanksAsync(country);
        }
        catch (Exception)
        {
            // Popularity is a nice-to-have; without it the list stays sorted by name.
            _popularityByCountry.Remove(key);
            return;
        }

        if (string.Equals(SelectedCountry == AllCountries ? WorldwidePopularity : SelectedCountry, key, StringComparison.OrdinalIgnoreCase))
        {
            await ApplySearchAsync();
        }
    }

    private void OnFavoritesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FavoritesHeader));
        OnPropertyChanged(nameof(HasNoFavorites));

        // Drag-reordering in the list is a Remove followed by an Add. Syncing after the current
        // dispatcher turn avoids closing and reopening that station's stream (and hearing an ad).
        if (!_favoritesSyncPending)
        {
            _favoritesSyncPending = true;
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, SyncFavorites);
        }
    }

    private void OnFavoriteTracksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FavoriteTracksHeader));
        OnPropertyChanged(nameof(HasNoFavoriteTracks));
        foreach (var entry in PlayHistory)
        {
            entry.IsSaved = FavoriteTracks.Any(t => t.IsSameSong(entry.Title));
        }

        UpdateNowPlaying();
        SaveSettings();
    }

    private void SyncFavorites()
    {
        _favoritesSyncPending = false;
        _engine.SetFavorites(Favorites.Select(f => f.Station));

        foreach (var favorite in Favorites)
        {
            if (_engine.Find(favorite.Station.Url) is { } stream)
            {
                RecordPlayed(stream);
                favorite.Status = stream.Status;
                ShowOnFavorite(favorite, stream);
            }
        }

        RefreshFavoriteMarks();
        UpdateLoudnessTexts();
        RefreshTimeShiftMemory();
        UpdateNowPlaying();
        ScheduleJumpListUpdate();
        SaveSettings();
    }

    /// <summary>Carries out a jump list item clicked while the app runs, or the one it was started with.</summary>
    public void Execute(JumpListCommand command)
    {
        switch (command.Action)
        {
            case JumpListAction.Play when Favorites.FirstOrDefault(f => f.Station.Url == command.Url) is { } favorite:
                Play(favorite.Station);
                break;
            case JumpListAction.Mute:
                IsMuted = true;
                break;
            case JumpListAction.Unmute:
                IsMuted = false;
                break;
        }
    }

    private void ScheduleJumpListUpdate()
    {
        // At most one update per interval, because songs change often across many favorites.
        if (!_jumpListTimer.IsRunning)
        {
            _jumpListTimer.Start();
        }
    }

    private void UpdateJumpList(bool withSongs)
    {
        var favorites = Favorites
            .Select(f => new JumpListItem(
                JumpListCommand.Title(f.Name, withSongs ? f.Song : ""),
                withSongs && f.HasSong ? Localizer.Format("JumpListListenToSong", f.Name, f.Song) : Localizer.Format("JumpListListenTo", f.Name),
                JumpListCommand.Play(f.Station.Url)))
            .ToList();
        var muted = withSongs && IsMuted;
        var muteTask = new JumpListItem(
            Localizer.Get(muted ? "JumpListUnmute" : "JumpListMute"),
            Localizer.Get(muted ? "JumpListUnmuteToolTip" : "JumpListMuteToolTip"),
            new JumpListCommand(muted ? JumpListAction.Unmute : JumpListAction.Mute),
            // The speaker icons of the Windows volume mixer: 1 is a speaker, 2 a muted speaker.
            Path.Combine(Environment.SystemDirectory, "SndVol.exe"),
            muted ? 1 : 2);

        var key = string.Join("\n", favorites.Append(muteTask));
        if (key == _jumpListShown)
        {
            return;
        }

        _jumpListShown = key;
        var category = Localizer.Get("JumpListFavorites");
        // In the background and in order, so a slow update never blocks the window or overwrites a newer one.
        _jumpListUpdates = _jumpListUpdates.ContinueWith(_ => TaskbarJumpList.Update(category, favorites, muteTask), TaskScheduler.Default);
    }

    private void MarkActiveResult(Station? active)
    {
        if (_activeResult is not null && _activeResult.Station.Url == active?.Url)
        {
            return;
        }

        if (_activeResult is not null)
        {
            _activeResult.IsActive = false;
        }

        _activeResult = active is null ? null : _allStations.FirstOrDefault(r => r.Station.Url == active.Url);
        if (_activeResult is not null)
        {
            _activeResult.IsActive = true;
        }
    }

    private void RefreshFavoriteMarks()
    {
        var favoriteUrls = Favorites.Select(f => f.Station.Url).ToHashSet(StringComparer.Ordinal);
        foreach (var result in _allStations)
        {
            result.IsFavorite = favoriteUrls.Contains(result.Station.Url);
        }
    }

    private void OnStreamStatusChanged(StationStream stream)
    {
        foreach (var favorite in Favorites.Where(f => f.Station.Url == stream.Station.Url))
        {
            favorite.Status = stream.Status;
        }

        if (stream == _engine.Active)
        {
            UpdateNowPlaying();
        }

        ZapOnAdBreak();
    }

    private void OnStreamMetadataChanged(StationStream stream)
    {
        RecordPlayed(stream);
        foreach (var favorite in Favorites.Where(f => f.Station.Url == stream.Station.Url))
        {
            ShowOnFavorite(favorite, stream);
        }

        if (stream == _engine.Active)
        {
            UpdateNowPlaying();
            // A break found at the live edge may lie just ahead of what is heard.
            ScheduleHeardChange();
        }

        ZapOnAdBreak();
    }

    private void UpdateNowPlaying()
    {
        var active = _engine.Active;
        foreach (var favorite in Favorites)
        {
            favorite.IsActive = active is not null && favorite.Station.Url == active.Station.Url;
        }

        MarkActiveResult(active?.Station);

        IsPlaying = active is not null;
        UpdateNowPlayingLogo(active?.Station ?? _lastPlayed);
        if (active is null)
        {
            NowPlayingName = _lastPlayed?.Name ?? Localizer.Get("ChooseStation");
            NowPlayingSong = "";
            NowPlayingTrack = null;
            IsNowPlayingTrackSaved = false;
            NowPlayingStatus = Localizer.Get(_lastPlayed is null ? "ClickFavoriteToListen" : "Stopped");
            return;
        }

        NowPlayingName = active.Station.Name;
        var isFavorite = Favorites.Any(f => f.Station.Url == active.Station.Url);
        // What is heard, which is behind the broadcast while the station is played from its buffer.
        var heard = _engine.HeardOf(active);
        NowPlayingSong = SongTexts.For(heard);
        NowPlayingTrack = heard is { IsInAdBreak: false, Metadata.Title: { } title } ? TrackTitle.Normalize(title) : null;
        IsNowPlayingTrackSaved = NowPlayingTrack is { } track && FavoriteTracks.Any(t => t.IsSameSong(track));
        NowPlayingStatus = StatusTexts.For(active.Status, isActive: true, heard.Sound)
                           + (_engine.IsTimeShifted ? " · " + Localizer.Format("NowPlayingBehindLive", FormatDelay(_engine.Delay)) : "")
                           + (IsMuted ? " · " + Localizer.Get("NowPlayingMuted") : "")
                           + (isFavorite ? "" : " · " + Localizer.Get("NowPlayingNotFavorite"))
                           + (active.Status is StreamStatus.Reconnecting or StreamStatus.Failed && active.LastError is { } error ? $" ({error})" : "");
    }

    /// <summary>A delay as minutes and seconds, such as "1:32", which reads the same in every language.</summary>
    private static string FormatDelay(TimeSpan delay) =>
        $"{(int)delay.TotalMinutes}:{delay.Seconds:00}";

    /// <summary>Resets and (re)loads the now-playing logo when the displayed station changes.</summary>
    private void UpdateNowPlayingLogo(Station? station)
    {
        if (station is null)
        {
            _nowPlayingLogoStationUrl = null;
            NowPlayingLogoUrl = null;
            NowPlayingLogo.Reset("");
            return;
        }

        if (_nowPlayingLogoStationUrl == station.Url)
        {
            return;
        }

        _nowPlayingLogoStationUrl = station.Url;
        NowPlayingLogoUrl = null;
        NowPlayingLogo.Reset(station.Name);
        _ = LoadNowPlayingLogoAsync(station);
    }

    private async Task LoadNowPlayingLogoAsync(Station station)
    {
        var url = await _logos.GetLogoUrlAsync(station);
        // Only apply it if this is still the displayed station once the lookup completes.
        if (_nowPlayingLogoStationUrl == station.Url)
        {
            NowPlayingLogo.SetLogoUrl(url);
            NowPlayingLogoUrl = url;
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        IsErrorOpen = true;
    }

    private void SaveSettings()
    {
        _settings.Favorites = Favorites.Select(f => f.Station).ToList();
        _settings.FavoriteTracks = FavoriteTracks.ToList();
        // Taken from the favorites, so a station that is no longer one does not stay in the list.
        _settings.NeverZapTo = Favorites.Where(f => !f.CanZapTo).Select(f => f.Station.Url).ToList();
        _settings.Volume = _engine.Volume;

        // A station that was played once should not keep its measurement in the settings file forever.
        var favorites = Favorites.Select(f => f.Station.Url).ToHashSet(StringComparer.Ordinal);
        foreach (var url in _settings.StationLoudness.Keys.Where(u => !favorites.Contains(u)).ToList())
        {
            _settings.StationLoudness.Remove(url);
        }

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(Localizer.Format("SettingsSaveFailed", ex.Message));
        }
    }

    public void Dispose()
    {
        SaveSettings();
        _historyPruneTimer.Stop();
        _heardTimer.Stop();
        SaveHistory();
        _updateTimer?.Stop();
        // A downloaded update is installed once this process has exited, whether the user closed the app or asked to restart it.
        _updater.InstallWhenClosed(_restartToUpdate);
        // Songs and the mute state are outdated once the app is closed.
        _jumpListTimer.Stop();
        UpdateJumpList(withSongs: false);
        _jumpListUpdates.Wait(TimeSpan.FromSeconds(5));
        _searchCts?.Cancel();
        _engine.Dispose();
        _proxy.Dispose();
        _classifier?.Dispose();
        _streamHttp.Dispose();
        _http.Dispose();
    }
}

/// <summary>The tabs next to each other above the right-hand panel of the full window.</summary>
public enum MainTab
{
    Stations,
    Zapper,
    FavoriteTracks,
    PlayHistory,
}

/// <summary>A length the time-shift buffer can be set to, in minutes (0 is off), and how the settings name it.</summary>
public sealed record TimeShiftOption(int Minutes, string Name);
