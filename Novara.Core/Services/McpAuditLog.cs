namespace Novara.Services;


public class McpAuditEvent
{
    public string Ts { get; set; } = "";
    public string Ev { get; set; } = "";
    public string Client { get; set; } = "";
    public string Path { get; set; } = "";
    public string Tool { get; set; } = "";
    public string Target { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Write { get; set; }
    public bool Ok { get; set; }
    public string Reason { get; set; } = "";
}

public static class McpAuditLog
{

    public const long RotateBytes = 2 * 1024 * 1024;
    private const int MaxArchiveFiles = 9;

    private static readonly JsonlAuditStore<McpAuditEvent> Store = new("mcp-audit", RotateBytes, MaxArchiveFiles);


    public static void SetBaseDir(string? dir) => Store.SetBaseDir(dir);




    public static void Write(McpAuditEvent evt)
    {
        if (evt == null) return;



        try
        {
            Prepare(evt);
            Store.Append(evt);
        }
        catch (Exception ex)
        {


            AuditWriteHealth.Report("mcp-audit write", ex);
        }
    }




    public static void WriteOrdered(McpAuditEvent evt)
    {
        if (evt == null) return;
        try
        {
            Prepare(evt);
            Store.AppendOrdered(evt);
        }
        catch (Exception ex)
        {
            AuditWriteHealth.Report("mcp-audit write", ex);
        }
    }




    public static void ClearAllWithMarker()
    {
        try
        {
            var marker = new McpAuditEvent { Ev = "audit_cleared", Target = "-", Ok = true };
            Prepare(marker);
            Store.ClearAllWithMarker(marker);
        }
        catch (Exception ex)
        {
            AuditWriteHealth.Report("mcp-audit clear", ex);
        }
    }


    internal static void WaitForPendingWrites() => Store.WaitForPendingWrites();


    private static void Prepare(McpAuditEvent evt)
    {
        if (string.IsNullOrEmpty(evt.Ts)) evt.Ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (evt.Client.Length == 0 && evt.Path.Length > 0) evt.Client = ShortName(evt.Path);
        evt.Title = Clean(evt.Title);
        evt.Reason = Clean(evt.Reason);


        evt.Path = Clean(evt.Path);
        evt.Client = Clean(evt.Client);
        evt.Tool = Clean(evt.Tool);
        evt.Target = Clean(evt.Target);
    }


    public static List<McpAuditEvent> ReadLatest(int max) => Store.ReadLatest(max);




    internal static void ClearAll() => Store.ClearAll();


    public static int CountAll() => Store.CountAll();

    public static string ShortName(string fullPath) => JsonlAuditStore<McpAuditEvent>.ShortName(fullPath);


    public static string TruncateTitle(string? title) => AuditSanitizer.TruncateTitle(title);






    private static string Clean(string? text) => AuditSanitizer.Clean(text);
}
