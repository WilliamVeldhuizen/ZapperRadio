using System.Runtime.InteropServices;
using System.Text;

namespace ZapperRadio.Shell;

/// <summary>The shell's .lnk object, shared by the jump list and <see cref="StableShortcuts"/>.</summary>
[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLink;

[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxLength, nint findData, uint flags);
    void GetIDList(out nint idList);
    void SetIDList(nint idList);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxLength);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxLength);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxLength);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
    void GetHotkey(out short hotkey);
    void SetHotkey(short hotkey);
    void GetShowCmd(out int showCmd);
    void SetShowCmd(int showCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxLength, out int iconIndex);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
    void Resolve(nint hwnd, uint flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
}
