using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using ZapperRadio.Updates;

namespace ZapperRadio.ViewModels;

/// <summary>Keeping the app up to date: a check shortly after the start and then every few hours, and the restart that installs what it found.</summary>
public sealed partial class MainViewModel
{
    /// <summary>The app usually runs for days, so waiting for the next start to look for an update would be too long.</summary>
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    private readonly AppUpdater _updater = new();
    private DispatcherQueueTimer? _updateTimer;
    private bool _restartToUpdate;

    /// <summary>Raised when the user wants the update installed now; the window closes, and the updater starts the app again.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>False for a build that was not installed by Velopack, which has no updates to look for.</summary>
    public bool IsUpdateSupported => _updater.IsSupported;

    /// <summary>What the last check found, as shown in the settings.</summary>
    [ObservableProperty]
    public partial string UpdateStatus { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    public partial bool IsCheckingForUpdates { get; set; }

    /// <summary>A newer version is downloaded and waits for the app to close.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    public partial bool IsUpdateReady { get; set; }

    /// <summary>The notice at the top of the window; the user can dismiss it, and the settings still offer the restart.</summary>
    [ObservableProperty]
    public partial bool IsUpdateNoticeOpen { get; set; }

    [ObservableProperty]
    public partial string UpdateNoticeTitle { get; set; } = "";

    private void StartUpdateChecks()
    {
        if (!_updater.IsSupported)
        {
            return;
        }

        // The first check waits, so it does not compete with the station list and the favorites for the network at the start.
        _updateTimer = _dispatcher.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromSeconds(30);
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Stop();
            _updateTimer.Interval = UpdateCheckInterval;
            _updateTimer.Start();
            _ = CheckForUpdatesCoreAsync();
        };
        _updateTimer.Start();
    }

    private bool CanCheckForUpdates => !IsCheckingForUpdates && !IsUpdateReady;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private Task CheckForUpdatesAsync() => CheckForUpdatesCoreAsync();

    private async Task CheckForUpdatesCoreAsync()
    {
        if (!CanCheckForUpdates || !_updater.IsSupported)
        {
            return;
        }

        IsCheckingForUpdates = true;
        UpdateStatus = Localizer.Get("UpdateChecking");
        try
        {
            // The download reports from another thread.
            var result = await _updater.CheckAsync(percent =>
                _dispatcher.TryEnqueue(() => UpdateStatus = Localizer.Format("UpdateDownloading", percent)));

            switch (result.Outcome)
            {
                case UpdateOutcome.Ready:
                    IsUpdateReady = true;
                    UpdateStatus = Localizer.Format("UpdateReady", result.Version!);
                    UpdateNoticeTitle = Localizer.Format("UpdateNoticeTitle", result.Version!);
                    IsUpdateNoticeOpen = true;
                    break;
                case UpdateOutcome.Failed:
                    UpdateStatus = Localizer.Format("UpdateFailed", result.Error!);
                    break;
                default:
                    UpdateStatus = Localizer.Get("UpdateUpToDate");
                    break;
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>Closes the app the normal way, which saves everything; <see cref="Dispose"/> then has the updater install the update and start the app again.</summary>
    [RelayCommand]
    private void RestartToUpdate()
    {
        _restartToUpdate = true;
        RestartRequested?.Invoke(this, EventArgs.Empty);
    }
}
