/* ========== ContextMenuService - Global Right-Click Menu ==========
Function: Register/unregister Novara entries in Windows right-click menus (desktop background / file / folder)
Corresponding UI: SettingsPage (toggle switch)
Logic Range: Whole file business logic of this module
*/
using System.IO;
using Microsoft.Win32;

namespace Novara.Services;

public static class ContextMenuService
{
    // HKCU\Software\Classes = user-level HKCR view; no admin required, Explorer picks entries up automatically.
    
    // registered entries of the installed release (Debug -> "NovaraOpen.Dev", Release -> "NovaraOpen").
    private static string MenuKeySuffix =>
#if DEBUG
        ".Dev";
#else
        "";
#endif

    private static string DesktopMenuKey => $@"Software\Classes\DesktopBackground\shell\NovaraOpen{MenuKeySuffix}";
    private static string FileMenuKey => $@"Software\Classes\*\shell\NovaraAddPath{MenuKeySuffix}";
    private static string FolderMenuKey => $@"Software\Classes\Directory\shell\NovaraAddPath{MenuKeySuffix}";
    // SystemFileAssociations binds by extension regardless of which ProgID currently owns .md,
    // so the entry survives users switching their default Markdown editor (VS Code / Typora / ...).
    private static string MdMenuKey => $@"Software\Classes\SystemFileAssociations\.md\shell\NovaraImport{MenuKeySuffix}";
    private static string MarkdownMenuKey => $@"Software\Classes\SystemFileAssociations\.markdown\shell\NovaraImport{MenuKeySuffix}";

    public static bool RegisterAll()
    {
        try
        {
            var exe = GetExecutablePath();
            var openCmd = $"\"{exe}\" --open";
            var addPathCmd = $"\"{exe}\" --add-path \"%1\"";
            var importMdCmd = $"\"{exe}\" --import-md \"%1\"";

            // E4-15: shell menu display names are localized (rendered with the current UI language at registration time)
            WriteMenu(DesktopMenuKey, App.GetString("Menu_OpenNovara"), openCmd, exe);
            WriteMenu(FileMenuKey, App.GetString("Menu_AddToNovaraPathBackup"), addPathCmd, exe);
            WriteMenu(FolderMenuKey, App.GetString("Menu_AddToNovaraPathBackup"), addPathCmd, exe);
            WriteMenu(MdMenuKey, App.GetString("Menu_ImportMdToNovara"), importMdCmd, exe);
            WriteMenu(MarkdownMenuKey, App.GetString("Menu_ImportMdToNovara"), importMdCmd, exe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool UnregisterAll()
    {
        try
        {
            DeleteTree(DesktopMenuKey);
            DeleteTree(FileMenuKey);
            DeleteTree(FolderMenuKey);
            DeleteTree(MdMenuKey);
            DeleteTree(MarkdownMenuKey);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Desktop menu key represents the whole group (all entries are written/removed together).
    public static bool IsRegistered()
    {
        try
        {
            // E4-30: all shell entries must exist - a half-registered state (e.g. file/folder keys
            // removed manually) must not show the toggle as "on". This also means installs upgraded
            // from builds without the .md entry read as "off" until the toggle is re-applied once.
            using var dk = Registry.CurrentUser.OpenSubKey(DesktopMenuKey);
            using var fk = Registry.CurrentUser.OpenSubKey(FileMenuKey);
            using var fok = Registry.CurrentUser.OpenSubKey(FolderMenuKey);
            using var mk = Registry.CurrentUser.OpenSubKey(MdMenuKey);
            using var mmk = Registry.CurrentUser.OpenSubKey(MarkdownMenuKey);
            return dk != null && fk != null && fok != null && mk != null && mmk != null;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteMenu(string menuKeyPath, string displayName, string commandLine, string exePath)
    {
        using var menuKey = Registry.CurrentUser.CreateSubKey(menuKeyPath);
        menuKey?.SetValue("", displayName);
        menuKey?.SetValue("Icon", exePath);
        using var cmdKey = Registry.CurrentUser.CreateSubKey(menuKeyPath + @"\command");
        cmdKey?.SetValue("", commandLine);
    }

    private static void DeleteTree(string keyPath)
    {
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
    }

    private static string GetExecutablePath()
    {
        var p = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(p) ? Path.Combine(AppContext.BaseDirectory, "Novara.exe") : p;
    }
}
