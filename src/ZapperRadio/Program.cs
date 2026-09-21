using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Velopack;
using ZapperRadio.Shell;

namespace ZapperRadio;

public static class Program
{
    [STAThread]
    private static int Main()
    {
        // First of all: when Velopack runs the app to install, update or uninstall it, this does that work and exits.
        VelopackApp.Build()
            // The Run key points into the install folder, which is deleted along with the app.
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.DisableForThisInstall())
            // So that a taskbar pin survives the next update; see StableShortcuts.
            .OnAfterInstallFastCallback(_ => StableShortcuts.PointAtLauncher())
            .OnAfterUpdateFastCallback(_ => StableShortcuts.PointAtLauncher())
            .Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();

        // One instance plays the radio. Starting the app again, for example from the jump list,
        // hands the command line to that instance instead of opening a second window.
        var mainInstance = AppInstance.FindOrRegisterForKey("ZapperRadio");
        if (!mainInstance.IsCurrent)
        {
            // Let the running instance bring its window to the front.
            AllowSetForegroundWindow(-1 /* ASFW_ANY */);
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => mainInstance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        // Also here, for installs from before it was done on install and update, and because an update
        // points the shortcut's icon back at the app.
        _ = Task.Run(StableShortcuts.PointAtLauncher);

        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
