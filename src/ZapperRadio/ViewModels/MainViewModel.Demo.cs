using ZapperRadio.Core.Models;
using ZapperRadio.Core.Streaming;
using ZapperRadio.Demo;
using ZapperRadio.Playback;

namespace ZapperRadio.ViewModels;

/// <summary>
/// The demo mode (see <see cref="DemoMode"/>): the window shows made-up favorites as the engine would report them,
/// without streaming anything. Picking a favorite listens to it in the window only.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The favorite the demo listens to, or null while it is stopped.</summary>
    private Station? _demoListening = DemoMode.IsOn ? DemoMode.Listening : null;

    /// <summary>Whether the demo plays the station it listens to from its buffer, as it does after the zap it starts with.</summary>
    private bool _demoTimeShifted = true;

    /// <summary>The station being listened to, in the demo or for real.</summary>
    private Station? ListeningTo => DemoMode.IsOn ? _demoListening : _engine.Active?.Station;

    private void ShowDemoFavorites()
    {
        foreach (var favorite in Favorites)
        {
            if (DemoMode.Find(favorite.Station.Url) is not { } demo)
            {
                continue;
            }

            favorite.Status = StreamStatus.Live;
            favorite.Sound = demo.Sound;
            favorite.IsAd = demo.IsAd;
            favorite.IsAssumedAdBreak = demo.IsAssumedAdBreak;
            favorite.Song = DemoSong(demo);
        }
    }

    private static string DemoSong(DemoStation demo) =>
        demo.IsAd ? Localizer.Get("SongAdvertisement")
        : demo.IsAssumedAdBreak ? Localizer.Get("SongProbablyAdBreak")
        : TrackTitle.Normalize(demo.Song);

    private void PlayDemo(Station station)
    {
        _lastPlayed = station;
        // A favorite picked by hand plays live, as it does outside the demo.
        _demoTimeShifted = false;
        _demoListening = station;
        UpdateNowPlaying();
    }

    private void StopDemo()
    {
        _demoListening = null;
        UpdateNowPlaying();
    }

    private void ShowDemoNowPlaying()
    {
        foreach (var favorite in Favorites)
        {
            favorite.IsActive = favorite.Station.Url == _demoListening?.Url;
        }

        MarkActiveResult(_demoListening);
        IsPlaying = _demoListening is not null;
        IsTimeShifted = _demoListening is not null && _demoTimeShifted;
        UpdateNowPlayingLogo(_demoListening ?? _lastPlayed);
        if (_demoListening is not { } station || DemoMode.Find(station.Url) is not { } demo)
        {
            NowPlayingName = _lastPlayed?.Name ?? Localizer.Get("ChooseStation");
            NowPlayingSong = "";
            NowPlayingTrack = null;
            IsNowPlayingTrackSaved = false;
            NowPlayingStatus = Localizer.Get(_lastPlayed is null ? "ClickFavoriteToListen" : "Stopped");
            return;
        }

        NowPlayingName = station.Name;
        NowPlayingSong = DemoSong(demo);
        NowPlayingTrack = demo.IsAd || demo.IsAssumedAdBreak || demo.Song.Length == 0 ? null : TrackTitle.Normalize(demo.Song);
        IsNowPlayingTrackSaved = NowPlayingTrack is { } track && FavoriteTracks.Any(t => t.IsSameSong(track));
        NowPlayingStatus = StatusTexts.For(StreamStatus.Live, isActive: true, demo.Sound)
                           + (IsTimeShifted ? " · " + Localizer.Format("NowPlayingBehindLive", FormatDelay(DemoMode.Delay)) : "")
                           + (IsMuted ? " · " + Localizer.Get("NowPlayingMuted") : "");
    }

    private void GoLiveDemo()
    {
        _demoTimeShifted = false;
        UpdateNowPlaying();
    }

    private async Task LoadDemoCatalogAsync()
    {
        _allStations = DemoMode.Catalog
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(s => new StationResultViewModel(s))
            .ToList();
        RefreshFavoriteMarks();
        _activeResult = null;
        MarkActiveResult(_demoListening);

        Countries = DemoMode.Catalog
            .Select(s => s.Country)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .Prepend(AllCountries)
            .ToList();
        SelectedCountry = AllCountries;
        OnPropertyChanged(nameof(SelectedCountry));
        CatalogStatus = Localizer.Format("CatalogStatus", _allStations.Count, DateTime.Today.ToString("d"));
        await ApplySearchAsync();
    }

    private void UpdateDemoLoudnessTexts()
    {
        foreach (var favorite in Favorites)
        {
            favorite.LoudnessText = DemoMode.Find(favorite.Station.Url) is { } demo
                ? LoudnessTexts.For(demo.Loudness, demo.GainDb, NormalizeLoudness)
                : LoudnessTexts.NotMeasured;
        }
    }

    /// <summary>What the buffers would take if every made-up favorite streamed at the most common bitrate.</summary>
    private long DemoBufferBytes(TimeSpan length) => (long)TimeShiftBuffer.CapacityFor(length, 128) * Favorites.Count;
}
