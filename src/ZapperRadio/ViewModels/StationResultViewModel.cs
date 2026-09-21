using CommunityToolkit.Mvvm.ComponentModel;
using ZapperRadio.Core.Models;

namespace ZapperRadio.ViewModels;

public sealed partial class StationResultViewModel(Station station) : ObservableObject
{
    public Station Station { get; } = station;

    public string Name => Station.Name;

    public string Subtitle => Station.Subtitle;

    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    /// <summary>Whether this is the station being listened to, marked like the favorite that plays.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>Whether the station failed its last check at radio-browser.info, so it most likely plays nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OfflinePrefix))]
    public partial bool IsOffline { get; private set; }

    /// <summary>When the station last worked, or null when it never did or is not offline.</summary>
    private DateTime? _lastWorked;

    /// <summary>Since when the station is down, said in front of its country and genre, or nothing while it works.</summary>
    public string OfflinePrefix
    {
        get
        {
            if (!IsOffline)
            {
                return "";
            }

            var text = _lastWorked is { } lastWorked
                ? Localizer.Format("OfflineSince", lastWorked.ToLocalTime())
                : Localizer.Get("OfflineAtLastCheck");
            return Subtitle.Length > 0 ? text + " · " : text;
        }
    }

    /// <param name="lastWorked">When it last worked, or null when it never did.</param>
    public void MarkOffline(bool offline, DateTime? lastWorked)
    {
        _lastWorked = offline ? lastWorked : null;
        if (IsOffline == offline)
        {
            OnPropertyChanged(nameof(OfflinePrefix));
        }

        IsOffline = offline;
    }
}
