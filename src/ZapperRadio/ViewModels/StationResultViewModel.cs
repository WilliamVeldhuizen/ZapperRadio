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
}
