using Microsoft.Win32;
using Windows.ApplicationModel;

namespace ZapperRadio.Shell;

public enum StartupState
{
    Off,
    On,

    /// <summary>
    /// The Store version only: the startup task was turned off in Windows' Startup Apps settings (or by a policy),
    /// and only Windows can turn it back on.
    /// </summary>
    TurnedOffInWindows,
}

/// <summary>
/// Whether ZapperRadio launches when the user signs in. The installed app from GitHub uses the per-user Run key,
/// so no admin rights are needed; the Store version cannot write that key and uses the package's startup task,
/// declared in Package.appxmanifest. Windows is the source of truth either way, so a change made from its own
/// Startup Apps settings instead of from here is picked up as well.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ZapperRadio";
    private const string TaskId = "ZapperRadioStartup";

    public static async Task<StartupState> GetStateAsync()
    {
        if (!AppPackage.IsPackaged)
        {
            return IsRunKeySet() ? StartupState.On : StartupState.Off;
        }

        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return ToState(task.State);
        }
        catch (Exception)
        {
            return StartupState.Off;
        }
    }

    /// <summary>Turns starting with Windows on or off, and returns what it ended up as, which for the Store version is up to Windows.</summary>
    public static async Task<StartupState> SetEnabledAsync(bool enabled)
    {
        if (!AppPackage.IsPackaged)
        {
            SetRunKey(enabled);
            return enabled ? StartupState.On : StartupState.Off;
        }

        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (enabled)
            {
                // Windows refuses, without asking, when the user turned the task off in its settings.
                return ToState(await task.RequestEnableAsync());
            }

            // A task enabled by policy cannot be disabled from the app.
            if (task.State == StartupTaskState.Enabled)
            {
                task.Disable();
            }

            return ToState(task.State);
        }
        catch (Exception)
        {
            return StartupState.Off;
        }
    }

    private static StartupState ToState(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => StartupState.On,
        StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy => StartupState.TurnedOffInWindows,
        _ => StartupState.Off,
    };

    private static bool IsRunKeySet()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>
    /// Turns it off when the entry starts the copy of the app that is running, which is what an uninstall wants:
    /// the entry would otherwise be left pointing at a file that is gone. An entry that starts another
    /// copy, such as an older MSI install next to this one, is not this copy's to remove.
    /// </summary>
    public static void DisableForThisInstall()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string command
            && string.Equals(command.Trim('"'), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static void SetRunKey(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
