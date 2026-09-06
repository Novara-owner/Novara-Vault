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
    // N4-14: CrashLogger's "key=" query/param form was never ported (plus token=) - audit titles/
    // reasons containing "...key=xxx" / "token=xxx" leaked third-party credentials verbatim.
    private static readonly Regex KeyParamRegex = new(@"((?:key|token)\s*=)[A-Za-z0-9._\-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        // N5-RC-03: Write runs inside Task.Run (McpService.Audit), so an uncaught IO failure
        // (disk full / AV file lock) would surface as an unobserved task exception - silently
        // lost and invisible. Swallow here to keep the best-effort contract honest.
        try
        {
            if (string.IsNullOrEmpty(evt.Ts)) evt.Ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (evt.Client.Length == 0 && evt.Path.Length > 0) evt.Client = ShortName(evt.Path);
            evt.Title = Clean(evt.Title);
            evt.Reason = Clean(evt.Reason);
            
            
            evt.Path = Clean(evt.Path);
            evt.Client = Clean(evt.Client);
            evt.Tool = Clean(evt.Tool);
            evt.Target = Clean(evt.Target);
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
                // N3-27: read each file as a STREAM and keep only the newest tail lines instead of
                // ReadAllLines-ing the whole ~20MB cap into memory every time the audit dialog opens.
                var archives = Directory.GetFiles(LogDir, "mcp-audit-*.log")
                    .OrderByDescending(f => f, StringComparer.Ordinal); // names embed a sortable timestamp

                // Current file first (newest), then archives newest -> oldest, until the page is full.
                if (File.Exists(CurrentFile))
                    AppendTailLines(CurrentFile, max, result);
                foreach (var archive in archives)
                {
                    if (result.Count >= max) break;
                    AppendTailLines(archive, max, result);
                }
            }
        }
        catch { }
        return result;
    }

    
    /// the parsed events to result newest-first until result reaches pageCap. Whole file is never
    /// materialized - memory stays bounded by the requested page size.</summary>
    private static void AppendTailLines(string file, int pageCap, List<McpAuditEvent> result)
    {
        try
        {
            var buffer = new List<string>(pageCap);
            foreach (var raw in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                buffer.Add(raw);
                if (buffer.Count > pageCap) buffer.RemoveAt(0); // keep only the newest pageCap lines
            }
            for (var i = buffer.Count - 1; i >= 0 && result.Count < pageCap; i--)
            {
                try
                {
                    var evt = JsonSerializer.Deserialize<McpAuditEvent>(buffer[i], Opts);
                    if (evt != null) result.Add(evt);
                }
                catch { /* torn/corrupt line - skip */ }
            }
        }
        catch { } // file rotated/deleted mid-stream - a partial tail is acceptable
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
        // N3-27: stream instead of ReadAllLines - counting must not materialize the whole file.
        try { return File.ReadLines(file).Count(l => l.Trim().Length > 0); }
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
        s = KeyParamRegex.Replace(s, "$1***"); // N4-14: key=/apikey=/api_key=/token= param form
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 300 ? s.Substring(0, 300) + "…" : s;
    }
}
