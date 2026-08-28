
using System;
using System.Diagnostics;
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
        var st = due.ToString("HH:mm");
        var sd = due.ToString("yyyy/MM/dd"); 
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
        }
        catch (Exception ex)
        {
            Debug.WriteLine("ReminderScheduler 失败: " + ex.Message);
        }
    }
}
