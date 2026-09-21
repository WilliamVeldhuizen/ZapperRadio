using Microsoft.Win32;

namespace ZapperRadio.Shell;

/// <summary>
/// Whether ZapperRadio launches when the user signs in, through the per-user Run key so no admin rights
/// are needed. The registry is the source of truth: it also reflects a user turning this off from
/// Windows' own Startup Apps settings instead of from here.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ZapperRadio";

    public static bool IsEnabled()
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

    public static void SetEnabled(bool enabled)
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
