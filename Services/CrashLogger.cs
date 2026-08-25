using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Novara.Services;

/// <summary>




/// </summary>
public static class CrashLogger
{
    private const int MaxLogFiles = 10;
    private static readonly object Gate = new();
    private static bool _initialized;

    private static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.DataDirName, "logs");

    
    private static string VersionString()
    {
        try
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (v != null) return v.ToString();
        }
        catch { }
        return "4.0";
    }

    /// <summary>N5W2-02: background-mode startup failures have no window/tray to show anything -
    /// persist a diagnostic log before the process exits (public entry, non-fatal by contract).</summary>
    public static void LogBackgroundStartupFailure(Exception ex) => Write(ex, "McpBackgroundStartup");

    
    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write(e.ExceptionObject as Exception ?? new Exception($"Unhandled: {e.ExceptionObject}"), "AppDomain");

        
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write(e.Exception, "TaskScheduler");
            e.SetObserved();
        };

        
        try
        {
            Microsoft.UI.Xaml.Application.Current.UnhandledException += (_, e) =>
            {
                Write(e.Exception, "Application");
                e.Handled = true;
            };
        }
        catch {  }
    }

    
    private static void Write(Exception ex, string source)
    {
        if (ex == null) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                var file = Path.Combine(LogDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
                File.WriteAllText(file, BuildReport(ex, source));

                
                var old = Directory.GetFiles(LogDir, "crash-*.txt")
                    .OrderByDescending(f => f)
                    .Skip(MaxLogFiles);
                foreach (var f in old)
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch { }
    }

    private static string BuildReport(Exception ex, string source)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"source : {source}");
        sb.AppendLine($"time   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"version: {VersionString()}");
        sb.AppendLine($"type   : {ex.GetType().FullName}");
        sb.AppendLine($"message: {Sanitize(ex.Message)}");
        sb.AppendLine($"stack  : {Sanitize(ex.StackTrace ?? "(no stack trace)")}");
        return sb.ToString();
    }

    
    private static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text;
        
        s = Regex.Replace(s, @"sk-[A-Za-z0-9_-]{4,}", "sk-***", RegexOptions.Compiled);
        // Authorization: Bearer xxxx / x-api-key: xxxx
        s = Regex.Replace(s, @"((?:bearer|x-api-key|api-key)\s*[:=]?\s*)[A-Za-z0-9._\-]{8,}", "$1***", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // ?key=xxxx / &key=xxxx
        s = Regex.Replace(s, @"(key=)[A-Za-z0-9._\-]{8,}", "$1***", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        return s;
    }
}
