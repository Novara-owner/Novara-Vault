using System.IO;
using Microsoft.Win32;

namespace Novara.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";


    private const string ValueName =
#if DEBUG
        "Novara.Dev";
#else
        "Novara";
#endif

    public static bool Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);


            key?.SetValue(ValueName, "\"" + GetExecutablePath() + "\"");
            return IsEnabled();
        }
        catch
        {
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetExecutablePath()
    {
        var p = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(p) ? Path.Combine(AppContext.BaseDirectory, "Novara.exe") : p;
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key?.GetValue(ValueName) is not string value || string.IsNullOrWhiteSpace(value)) return false;

            value = value.Trim('"');
            value = Environment.ExpandEnvironmentVariables(value);
            return File.Exists(value);
        }
        catch
        {
            return false;
        }
    }
}
