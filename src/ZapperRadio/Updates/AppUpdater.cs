using Velopack;
using Velopack.Sources;
using ZapperRadio.Shell;

namespace ZapperRadio.Updates;

public enum UpdateOutcome
{
    /// <summary>The app was not installed by Velopack (a development build, say), so it cannot update itself.</summary>
    NotInstalled,
    UpToDate,

    /// <summary>A newer version is downloaded and is installed when the app closes.</summary>
    Ready,
    Failed,
}

/// <param name="Version">The version that is ready, for <see cref="UpdateOutcome.Ready"/>.</param>
/// <param name="Error">What went wrong, for <see cref="UpdateOutcome.Failed"/>.</param>
public sealed record UpdateResult(UpdateOutcome Outcome, string? Version = null, string? Error = null);

/// <summary>
/// Looks for a newer version among the GitHub releases, downloads it, and installs it when the app closes.
/// Velopack keeps the previous version until the new one is in place, so an update that goes wrong cannot break the app.
/// </summary>
public sealed class AppUpdater
{
    private const string RepositoryUrl = "https://github.com/WilliamVeldhuizen/ZapperRadio";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _ready;

    public AppUpdater()
    {
        // The Store version is updated by the Store, and never looks at the GitHub releases.
        if (AppPackage.IsPackaged)
        {
            return;
        }

        try
        {
            var manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
            _manager = manager.IsInstalled ? manager : null;
        }
        catch (Exception)
        {
            // Not being able to update is no reason for the radio not to play.
        }
    }

    /// <summary>False for the Store version, and when the app was not installed by Velopack: there is nothing to update then.</summary>
    public bool IsSupported => _manager is not null;

    /// <summary>The version that is downloaded and waiting for the app to close, if any.</summary>
    public string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();

    public async Task<UpdateResult> CheckAsync(Action<int>? progress = null)
    {
        if (_manager is null)
        {
            return new UpdateResult(UpdateOutcome.NotInstalled);
        }

        // Nothing newer can turn up while one is already waiting for the restart.
        if (_ready is not null)
        {
            return new UpdateResult(UpdateOutcome.Ready, ReadyVersion);
        }

        try
        {
            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
            {
                return new UpdateResult(UpdateOutcome.UpToDate);
            }

            await _manager.DownloadUpdatesAsync(update, progress);
            _ready = update;
            return new UpdateResult(UpdateOutcome.Ready, ReadyVersion);
        }
        catch (Exception ex)
        {
            // Offline, rate limited by GitHub, a download that was cut off: the next check tries again.
            return new UpdateResult(UpdateOutcome.Failed, Error: ex.Message);
        }
    }

    /// <summary>
    /// Has Velopack install the downloaded version as soon as this process has exited, and start the app again
    /// when <paramref name="restart"/> is set. Does nothing when no update is waiting. The caller still has to
    /// close the app: this only tells the updater what to do once that has happened.
    /// </summary>
    public void InstallWhenClosed(bool restart)
    {
        if (_manager is null || _ready is null)
        {
            return;
        }

        try
        {
            _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart);
        }
        catch (Exception)
        {
            // The update is still on disk and is applied the next time the app starts.
        }
    }
}
