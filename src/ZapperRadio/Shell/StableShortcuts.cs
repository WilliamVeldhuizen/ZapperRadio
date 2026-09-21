using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Velopack.Locators;

namespace ZapperRadio.Shell;

/// <summary>
/// Points the app's shortcuts, and with them a taskbar pin, at Velopack's launcher in the install folder instead
/// of at the app in its "current" folder. An update renames that folder away and deletes it, and Windows follows
/// the rename: a pin on the app ends up pointing into a folder that is gone, and the taskbar drops it. The launcher
/// is overwritten in place and never moves, so a pin on it survives every update. Velopack only retargets a
/// shortcut whose target is missing, so once pointed at the launcher it stays there.
/// </summary>
public static class StableShortcuts
{
    private const int StgmReadWrite = 2;

    /// <summary>Does nothing for a build that Velopack did not install, such as one started from Visual Studio.</summary>
    public static void PointAtLauncher()
    {
        try
        {
            if (!VelopackLocator.IsCurrentSet || Environment.ProcessPath is not { } app)
            {
                return;
            }

            var locator = VelopackLocator.Current;
            if (locator.IsPortable || locator.RootAppDir is not { } root || !File.Exists(locator.UpdateExePath))
            {
                return;
            }

            var launcher = Path.Combine(root, Path.GetFileName(app));
            if (!File.Exists(launcher) || string.Equals(launcher, app, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var pinned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned");
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), pinned })
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (var lnk in Directory.EnumerateFiles(folder, "*.lnk", options))
                {
                    Retarget(lnk, app, launcher, root);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            // A shortcut left pointing at the app still starts it; only its pin is at risk on the next update.
        }
    }

    private static void Retarget(string lnk, string app, string launcher, string root)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            var file = (IPersistFile)link;
            file.Load(lnk, StgmReadWrite);

            var target = new StringBuilder(260);
            link.GetPath(target, target.Capacity, 0, 0);
            var isApp = string.Equals(target.ToString(), app, StringComparison.OrdinalIgnoreCase);
            if (!isApp && !string.Equals(target.ToString(), launcher, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Velopack sets the icon back to the app on every update, which has the same folder problem.
            var icon = new StringBuilder(260);
            link.GetIconLocation(icon, icon.Capacity, out var iconIndex);
            var iconIsLauncher = iconIndex == 0 && string.Equals(icon.ToString(), launcher, StringComparison.OrdinalIgnoreCase);
            if (!isApp && iconIsLauncher)
            {
                return;
            }

            link.SetPath(launcher);
            link.SetWorkingDirectory(root);
            link.SetIconLocation(launcher, 0);
            file.Save(null, fRemember: true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }
}
