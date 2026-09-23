using System.Runtime.InteropServices;

namespace ZapperRadio.Shell;

/// <summary>
/// Tells the Microsoft Store build, which runs as an MSIX package with a package identity, from the regular
/// unpackaged build installed from GitHub. The two differ in how they start with Windows and in how they update.
/// </summary>
public static class AppPackage
{
    private const int AppModelErrorNoPackage = 15700;

    /// <summary>True when the app runs from its MSIX package, as the Store version does.</summary>
    public static bool IsPackaged { get; } = HasPackageIdentity();

    private static bool HasPackageIdentity()
    {
        // With no buffer this only asks for the length: a packaged process gets ERROR_INSUFFICIENT_BUFFER.
        var length = 0;
        return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
