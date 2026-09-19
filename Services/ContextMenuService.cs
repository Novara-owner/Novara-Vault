using System.IO;
using Microsoft.Win32;

namespace Novara.Services;

public static class ContextMenuService
{



    private static string MenuKeySuffix =>
#if DEBUG
        ".Dev";
#else
        "";
#endif

    private static string DesktopMenuKey => $@"Software\Classes\DesktopBackground\shell\NovaraOpen{MenuKeySuffix}";
    private static string FileMenuKey => $@"Software\Classes\*\shell\NovaraAddPath{MenuKeySuffix}";
    private static string FolderMenuKey => $@"Software\Classes\Directory\shell\NovaraAddPath{MenuKeySuffix}";


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


    public static bool IsRegistered()
    {
        try
        {



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
