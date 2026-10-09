using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Novara.Services;






internal static class FilePicker
{
    private const uint FOS_PICKFOLDERS = 0x20;
    private const uint FOS_FORCEFILESYSTEM = 0x40;
    private const uint FOS_OVERWRITEPROMPT = 0x2;
    private const uint SIGDN_FILESYSPATH = 0x80058000;
    private const int HR_CANCELLED = unchecked((int)0x800704C7);
    private static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");


    public static string? PickFile(string? filters = null, string? title = null)
    {
        var paths = Core(folders: false, multi: false, filters: filters, title: title);
        return paths.Count > 0 ? paths[0] : null;
    }


    public static List<string> PickFiles(string filters, string? title = null)
        => Core(folders: false, multi: true, filters: filters, title: title);


    public static string? PickFolder(string? title = null, bool startAtDesktop = false)
    {
        var paths = Core(folders: true, multi: false, filters: null, title: title, startAtDesktop: startAtDesktop);
        return paths.Count > 0 ? paths[0] : null;
    }


    public static string? PickSaveFile(string defaultName, (string Name, string[] Extensions)[] choices, string? title = null)
    {
        var dlg = (IFileSaveDialog)new FileSaveDialogRCW();
        dlg.GetOptions(out var fos);
        fos |= FOS_FORCEFILESYSTEM | FOS_OVERWRITEPROMPT;
        dlg.SetOptions(fos);
        if (choices is { Length: > 0 })
        {
            dlg.SetFileTypes((uint)choices.Length, choices.Select(c => new FilterSpec { Name = c.Name, Spec = string.Join(";", c.Extensions) }).ToArray());
            dlg.SetFileTypeIndex(1);
        }
        if (!string.IsNullOrWhiteSpace(defaultName)) dlg.SetFileName(defaultName);
        var hwnd = App.MainWindow is null ? nint.Zero : WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var hr = dlg.Show(hwnd);
        if (hr == HR_CANCELLED) return null;
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        dlg.GetResult(out var item);
        return ItemPath(item);
    }

    private static List<string> Core(bool folders, bool multi, string? filters, string? title, bool startAtDesktop = false)
    {
        var result = new List<string>();
        var dlg = (IFileOpenDialog)new FileOpenDialogRCW();
        dlg.GetOptions(out var fos);
        fos |= FOS_FORCEFILESYSTEM;
        if (folders) fos |= FOS_PICKFOLDERS;
        dlg.SetOptions(fos);
        if (!string.IsNullOrWhiteSpace(title)) dlg.SetTitle(title);
        if (!folders)
        {
            var spec = string.IsNullOrWhiteSpace(filters) ? "*.*" : filters;
            dlg.SetFileTypes(1, new[] { new FilterSpec { Name = spec, Spec = spec } });
            dlg.SetFileTypeIndex(1);
        }
        if (startAtDesktop && SHCreateItemFromParsingName("shell:Desktop", 0, IID_IShellItem, out var folder) == 0)
            dlg.SetFolder(folder);

        var hwnd = App.MainWindow is null ? nint.Zero : WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var hr = dlg.Show(hwnd);
        if (hr == HR_CANCELLED) return result;
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);

        if (!multi)
        {
            dlg.GetResult(out var item);
            var path = ItemPath(item);
            if (path != null) result.Add(path);
            return result;
        }
        dlg.GetResults(out var items);
        while (items.Next(1, out var one, out _) == 0)
        {
            var path = ItemPath(one);
            if (path != null) result.Add(path);
        }
        return result;
    }

    private static string? ItemPath(IShellItem item)
    {
        if (item.GetDisplayName(SIGDN_FILESYSPATH, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FilterSpec
    {
        public string Name;
        public string Spec;
    }

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW { }

    [ComImport, Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
    private class FileSaveDialogRCW { }

    [ComImport, Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileSaveDialog
    {
        [PreserveSig] int Show(nint hwndOwner);

        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] FilterSpec[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        [PreserveSig] int GetFileTypeIndex(out uint piFileType);
        void Advise(nint pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        [PreserveSig] int GetFolder(out IShellItem ppsi);
        [PreserveSig] int GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] int GetFileName(out nint ppszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        [PreserveSig] int SetClientGuid(ref Guid guid);
        [PreserveSig] int ClearClientData();
        [PreserveSig] int SetFilter(nint pFilter);
        void SetSaveAsItem(IShellItem psi);
        [PreserveSig] int GetProperties(out nint ppStore);
        [PreserveSig] int SetProperties(nint pStore);
        [PreserveSig] int ApplyProperties(IShellItem psi, nint pStore, nint hwnd, nint pSink);
    }

    [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(nint hwndOwner);

        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] FilterSpec[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        [PreserveSig] int GetFileTypeIndex(out uint piFileType);
        void Advise(nint pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        [PreserveSig] int GetFolder(out IShellItem ppsi);
        [PreserveSig] int GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] int GetFileName(out nint ppszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        [PreserveSig] int SetClientGuid(ref Guid guid);
        [PreserveSig] int ClearClientData();
        [PreserveSig] int SetFilter(nint pFilter);
        [PreserveSig] int GetResults(out IEnumShellItems ppenumShellItems);
        [PreserveSig] int GetSelectedItems(out nint ppsai);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, out nint ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("70629033-E363-4A28-A567-0DB78006E6D7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint celt, out IShellItem rgelt, out nint pceltFetched);
        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems ppenum);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string pszPath, nint pbc, in Guid riid, out IShellItem ppv);
}
