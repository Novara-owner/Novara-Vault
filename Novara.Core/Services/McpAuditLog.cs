/* ========== McpAuditLog - MCP Agent Activity Audit Trail ==========
Function: Append-only JSONL audit log for the local MCP endpoint. Records connection
auth events (ok / first-grant / denial reasons) and every tool call (tool, target,
title snapshot, read/write, outcome). Pure observation: writes never throw into the
caller and never block the pipe threads beyond a short lock shared with rotation.
Corresponding UI: SettingsPage MCP card -> audit records dialog
Logic Range: Whole file business logic of this module
*/
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Novara.Services;

/// <summary>One audit entry (JSONL line). Fields are always serialized so the schema stays stable.</summary>
public class McpAuditEvent
{
    public string Ts { get; set; } = "";      // local time, yyyy-MM-dd HH:mm:ss
    public string Ev { get; set; } = "";      // auth_ok | auth_new | auth_denied | call | revoke
    public string Client { get; set; } = "";  // agent process file name (display column)
    public string Path { get; set; } = "";    // full authorized/rejected process path
    public string Tool { get; set; } = "";    // MCP tool name (call events)
    public string Target { get; set; } = "";  // "type:id8" / list scope / "-"
    public string Title { get; set; } = "";   // entity title snapshot, truncated + sanitized
    public bool Write { get; set; }           // mutating call?
    public bool Ok { get; set; }              // allowed & succeeded?
    public string Reason { get; set; } = "";  // denial / error message (sanitized)
}

public static class McpAuditLog
{
    
    public const long RotateBytes = 2 * 1024 * 1024;
    private const int MaxArchiveFiles = 9;
    private const int MaxTitleChars = 60;

    private static readonly object Gate = new();
    private static string? _baseDirOverride; // tests only (PasswordService.SetBaseDir pattern)

    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Mirrors CrashLogger.Sanitize: token-shaped secrets must never reach the log file.
    private static readonly Regex SkRegex = new(@"sk-[A-Za-z0-9_-]{4,}", RegexOptions.Compiled);
    private static readonly Regex AuthHeaderRegex = new(@"((?:bearer|x-api-key|api-key)\s*[:=]?\s*)[A-Za-z0-9._\-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string LogDir => _baseDirOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CoreEnv.DataDirName, "logs");

    private static string CurrentFile => Path.Combine(LogDir, "mcp-audit.log");

    /// <summary>Test seam: redirect the log directory (null restores the real data dir).</summary>
    public static void SetBaseDir(string? dir) => _baseDirOverride = dir;

    /// <summary>Append one event. Best-effort by contract: any failure is swallowed silently.</summary>
    public static void Write(McpAuditEvent evt)
    {
        if (evt == null) return;
        try
        {
            if (string.IsNullOrEmpty(evt.Ts)) evt.Ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (evt.Client.Length == 0 && evt.Path.Length > 0) evt.Client = ShortName(evt.Path);
            evt.Title = Clean(evt.Title);
            evt.Reason = Clean(evt.Reason);
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                var cur = new FileInfo(CurrentFile);
                if (cur.Exists && cur.Length >= RotateBytes)
                    File.Move(CurrentFile, Path.Combine(LogDir, $"mcp-audit-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log"));

                File.AppendAllText(CurrentFile, JsonSerializer.Serialize(evt, Opts) + "\n", new UTF8Encoding(false));

                // Rolling cleanup: keep at most MaxArchiveFiles timestamped archives besides the current file.
                var old = Directory.GetFiles(LogDir, "mcp-audit-*.log")
                    .OrderByDescending(f => f, StringComparer.Ordinal)
                    .Skip(MaxArchiveFiles);
                foreach (var f in old) { try { File.Delete(f); } catch { } }
            }
        }
        catch { }
    }

    /// <summary>Read up to max entries, newest first (UI ordering). Broken lines are skipped.</summary>
    public static List<McpAuditEvent> ReadLatest(int max)
    {
        var result = new List<McpAuditEvent>();
        if (max <= 0) return result;
        try
        {
            lock (Gate)
            {
                if (!Directory.Exists(LogDir)) return result;
                var current = File.Exists(CurrentFile) ? File.ReadAllLines(CurrentFile) : Array.Empty<string>();
                // Ordinal sort works because archive names embed a sortable timestamp.
                var archives = Directory.GetFiles(LogDir, "mcp-audit-*.log")
                    .OrderByDescending(f => f, StringComparer.Ordinal);

                IEnumerable<string> NewToOld()
                {
                    for (var i = current.Length - 1; i >= 0; i--) yield return current[i];
                    foreach (var archive in archives)
                        foreach (var line in File.ReadAllLines(archive).Reverse()) yield return line;
                }

                foreach (var line in NewToOld())
                {
                    if (result.Count >= max) break;
                    try
                    {
                        var evt = JsonSerializer.Deserialize<McpAuditEvent>(line, Opts);
                        if (evt != null) result.Add(evt);
                    }
                    catch { /* torn/corrupt line - skip */ }
                }
            }
        }
        catch { }
        return result;
    }

    /// <summary>Delete every audit file (current + archives). Called from the settings dialog.</summary>
    public static void ClearAll()
    {
        try
        {
            lock (Gate)
            {
                if (!Directory.Exists(LogDir)) return;
                foreach (var f in Directory.GetFiles(LogDir, "mcp-audit*.log")) { try { File.Delete(f); } catch { } }
            }
        }
        catch { }
    }

    /// <summary>Total event count across current + archives (file-bound, cheap enough for the card).</summary>
    public static int CountAll()
    {
        try
        {
            lock (Gate)
            {
                if (!Directory.Exists(LogDir)) return 0;
                var n = 0;
                if (File.Exists(CurrentFile)) n += CountLines(CurrentFile);
                foreach (var a in Directory.GetFiles(LogDir, "mcp-audit-*.log")) n += CountLines(a);
                return n;
            }
        }
        catch { return 0; }
    }

    private static int CountLines(string file)
    {
        try { return File.ReadAllLines(file).Count(l => l.Trim().Length > 0); }
        catch { return 0; }
    }

    public static string ShortName(string fullPath)
    {
        try { return Path.GetFileName(fullPath.TrimEnd('\\', '/')); }
        catch { return fullPath; }
    }

    /// <summary>Title snapshot rule (D1): trim + hard-truncate to 60 chars with ellipsis.</summary>
    public static string TruncateTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var t = title.Trim();
        return t.Length <= MaxTitleChars ? t : t.Substring(0, MaxTitleChars) + "…";
    }

    /// <summary>Mask secret-shaped substrings; also caps length so a hostile detail cannot bloat a line.</summary>
    private static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = SkRegex.Replace(text, "sk-***");
        s = AuthHeaderRegex.Replace(s, "$1***");
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 300 ? s.Substring(0, 300) + "…" : s;
    }
}
