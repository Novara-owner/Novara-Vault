
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Novara.Services;

public static class ToastService
{
    private const string AppId = "Novara.App"; // AUMID
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered) return;
        try
        {
            EnsureShortcut();
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("ToastService 注册失败: " + ex.Message);
        }
    }

    
    public static void Show(string title, string content)
    {
        try
        {
            EnsureRegistered();
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(content)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("ToastService 发送失败: " + ex.Message);
        }
    }

    
    private static void EnsureShortcut()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs");
        Directory.CreateDirectory(dir);
        var lnk = Path.Combine(dir, "Novara.lnk");
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Novara.exe");
        ShortcutHelper.CreateWithAppId(lnk, exe, AppId);
    }
}


internal static class ShortcutHelper
{
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");

    public static void CreateWithAppId(string lnkPath, string targetPath, string appId)
    {
        var type = Type.GetTypeFromCLSID(ClsidShellLink, true);
        if (type == null) return;
        var link = (IShellLinkW)Activator.CreateInstance(type)!;
        link.SetPath(targetPath);
        link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? "");
        if (link is IPersistFile pf)
            pf.Save(lnkPath, true);

        if (link is IPropertyStore store)
        {
            using var pv = new PropVariant(appId);
            var pkey = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            store.SetValue(ref pkey, pv);
            store.Commit();
        }
    }

    private readonly struct PropertyKey
    {
        public readonly Guid Fmtid;
        public readonly int Pid;
        public PropertyKey(Guid fmtid, int pid) { Fmtid = fmtid; Pid = pid; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropVariant : IDisposable
    {
        private readonly ushort _vt;
        private readonly ushort _wReserved1, _wReserved2, _wReserved3;
        private readonly IntPtr _ptr;
        public PropVariant(string value)
        {
            _vt = 31; // VT_LPWSTR
            _wReserved1 = _wReserved2 = _wReserved3 = 0;
            _ptr = Marshal.StringToCoTaskMemUni(value);
        }
        public void Dispose() { if (_ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(_ptr); }
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, PropVariant pv);
        void Commit();
    }
}
