using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ReminderX.Services;

/// <summary>
/// Creates (once) a Start Menu shortcut carrying an explicit AppUserModelID so that
/// Windows toast notifications can be attributed to ReminderX even though it is an
/// unpackaged Win32/WPF app. Required by Microsoft.Toolkit.Uwp.Notifications.
/// </summary>
public static class ShortcutHelper
{
    public const string AppId = "WorldEmblem.ReminderX";

    public static void EnsureShortcutInstalled()
    {
        try
        {
            var shortcutPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "ReminderX.lnk");

            var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

            if (File.Exists(shortcutPath))
                return;

            InstallShortcut(shortcutPath, exePath, AppId);
        }
        catch (Exception ex)
        {
            Logger.Log($"ShortcutHelper failed: {ex.Message}");
        }
    }

    private static void InstallShortcut(string shortcutPath, string exePath, string appId)
    {
        var shellLink = (IShellLinkW)new CShellLink();
        shellLink.SetPath(exePath);
        shellLink.SetArguments("");
        shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? "");

        var propertyStore = (IPropertyStore)shellLink;
        var variant = new PropVariant { vt = (ushort)VarEnum.VT_LPWSTR, pwszVal = Marshal.StringToCoTaskMemUni(appId) };
        propertyStore.SetValue(ref PKEY_AppUserModel_ID, ref variant);
        propertyStore.Commit();

        var persistFile = (IPersistFile)shellLink;
        persistFile.Save(shortcutPath, true);

        Marshal.FreeCoTaskMem(variant.pwszVal);
    }

    private static PropertyKey PKEY_AppUserModel_ID = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5
    };

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(StringBuilder pszName, int cchMaxName);
        void SetDescription(string pszName);
        void GetWorkingDirectory(StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory(string pszDir);
        void GetArguments(StringBuilder pszArgs, int cchMaxPath);
        void SetArguments(string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation(string pszIconPath, int iIcon);
        void SetRelativePath(string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath(string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1, wReserved2, wReserved3;
        public IntPtr pwszVal;
        public int int32Padding1;
        public int int32Padding2;
    }
}
