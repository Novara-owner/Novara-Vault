




using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Novara.Services;

public static class ReminderScheduler
{
    private static string TaskPrefix =>
#if DEBUG
        "NovaraReminder.Dev_";
#else
        "NovaraReminder_";
#endif

    private static string GetExecutablePath()
    {
        var p = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(p) ? Path.Combine(AppContext.BaseDirectory, "Novara.exe") : p;
    }

    
    public static void Schedule(Guid id, DateTime due)
    {
        var taskName = TaskPrefix + id;
        
        
        
        var st = due.ToString("t", CultureInfo.CurrentCulture);
        var sd = due.ToString("d", CultureInfo.CurrentCulture);
        var tr = "\\\"" + GetExecutablePath() + "\\\" --reminder " + id;
        var args = "/create /tn \"" + taskName + "\" /tr \"" + tr + "\" /sc once /st " + st + " /sd " + sd + " /f";
        Run(args);
    }

    
    public static void Cancel(Guid id)
    {
        Run("/delete /tn \"" + TaskPrefix + id + "\" /f");
    }

    private static void Run(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            p?.WaitForExit(5000);
            // N4P-08: surface failures (locked task folder, ACL) instead of swallowing the exit
            // code silently - scheduling stays best-effort, but the reason is now diagnosable.
            if (p is { HasExited: true } && p.ExitCode != 0)
                Debug.WriteLine($"ReminderScheduler 失败(exit {p.ExitCode}): schtasks {args}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("ReminderScheduler 失败: " + ex.Message);
        }
    }
}
