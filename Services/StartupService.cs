/* ========== StartupService - Auto-Start ==========
Function: Windows registry Run key management - enable/disable/query real autostart state
Corresponding UI: StartupService.cs
Logic Range: Whole file business logic of this module
*/
using System.IO;
using Microsoft.Win32;

namespace Novara.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // E1-14: run-key value isolated per config so a dev build cannot overwrite the installed
    // release's autostart entry (Debug="Novara.Dev", Release="Novara").
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
            // E4-31: quote the exe path - the installed layout (Program Files) contains spaces and an
            // unquoted Run value fails to launch at logon; IsEnabled() already trims quotes.
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
