using System.Runtime.InteropServices;
using System.Text;
using ZapperRadio.Core.Shell;

namespace ZapperRadio.Shell;

/// <summary>An item on the jump list: a favorite station or the mute task.</summary>
public sealed record JumpListItem(string Title, string ToolTip, JumpListCommand Command, string? IconPath = null, int IconIndex = 0);

/// <summary>
/// Fills the list shown when right-clicking the taskbar button, through the shell's COM interfaces,
/// because the WinRT JumpList class only works for packaged apps.
/// </summary>
public static class TaskbarJumpList
{
    private static readonly object Gate = new();

    /// <summary>
    /// Replaces the jump list. The name of the favorites category is handed in, rather than looked up here,
    /// because this runs on a background thread and the language is chosen on the UI thread.
    /// </summary>
    public static void Update(string favoritesCategory, IReadOnlyList<JumpListItem> favorites, JumpListItem muteTask)
    {
        lock (Gate)
        {
            var list = (ICustomDestinationList)new DestinationList();
            try
            {
                // These shell objects fail a query for IObjectArray (E_NOINTERFACE), so IObjectCollection, which extends it, is used throughout.
                var unknownId = new Guid("00000000-0000-0000-C000-000000000046");
                list.BeginList(out var maxSlots, ref unknownId, out var removedObject);
                var removed = ArgumentsOf(removedObject as IObjectCollection);

                // Items the user removed from the list may not be added again; they return after changing favorites.
                var shown = favorites.Where(f => !removed.Contains(f.Command.ToArguments())).Take((int)maxSlots).ToList();
                var tasks = new List<JumpListItem>();
                if (shown.Count > 0 && list.AppendCategory(favoritesCategory,ToCollection(shown)) < 0)
                {
                    // Windows refuses custom categories when "Show recently opened items" is turned off.
                    tasks.AddRange(shown);
                }

                tasks.Add(muteTask);
                list.AddUserTasks(ToCollection(tasks));
                list.CommitList();
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                // The jump list is a convenience; the app works fine without it.
                try { list.AbortList(); } catch (COMException) { }
            }
            finally
            {
                Marshal.ReleaseComObject(list);
            }
        }
    }

    private static HashSet<string> ArgumentsOf(IObjectCollection? items)
    {
        var arguments = new HashSet<string>(StringComparer.Ordinal);
        if (items is null)
        {
            return arguments;
        }

        var linkId = typeof(IShellLinkW).GUID;
        items.GetCount(out var count);
        for (uint i = 0; i < count; i++)
        {
            items.GetAt(i, ref linkId, out var item);
            var text = new StringBuilder(2048);
            ((IShellLinkW)item).GetArguments(text, text.Capacity);
            arguments.Add(text.ToString());
        }

        return arguments;
    }

    private static IObjectCollection ToCollection(IEnumerable<JumpListItem> items)
    {
        var collection = (IObjectCollection)new EnumerableObjectCollection();
        var exe = Environment.ProcessPath!;
        foreach (var item in items)
        {
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(exe);
            link.SetArguments(item.Command.ToArguments());
            link.SetIconLocation(item.IconPath ?? exe, item.IconPath is null ? 0 : item.IconIndex);
            link.SetDescription(item.ToolTip);

            var title = new PropVariant { ValueType = 31 /* VT_LPWSTR */, Value = Marshal.StringToCoTaskMemUni(item.Title) };
            try
            {
                var store = (IPropertyStore)link;
                var titleKey = new PropertyKey(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2); // PKEY_Title
                store.SetValue(ref titleKey, ref title);
                store.Commit();
            }
            finally
            {
                Marshal.FreeCoTaskMem(title.Value);
            }

            collection.AddObject(link);
        }

        return collection;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort ValueType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Value;
        public nint Value2;
    }

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")]
    private class DestinationList;

    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")]
    private class EnumerableObjectCollection;

    [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void BeginList(out uint maxSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removedItems);
        [PreserveSig] int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectCollection items);
        void AppendKnownCategory(int category);
        void AddUserTasks(IObjectCollection tasks);
        void CommitList();
        void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removedItems);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void AbortList();
    }

    [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
        void AddObject([MarshalAs(UnmanagedType.Interface)] object item);
        void AddFromArray(IObjectCollection items);
        void RemoveObjectAt(uint index);
        void Clear();
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
}
