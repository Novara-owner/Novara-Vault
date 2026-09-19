namespace Novara.Services;






public static class SyncAuditEvents
{
    public const string Pair = "pair";
    public const string Unpair = "unpair";
    public const string Push = "push";
    public const string Pull = "pull";
    public const string Conflict = "conflict";
    public const string KeepLocal = "keep_local";
    public const string TakeRemote = "take_remote";
    public const string Export = "export";
    public const string KeyView = "key_view";
    public const string ReadTokenCreate = "read_token_create";
    public const string ReadTokenRevoke = "read_token_revoke";
    public const string EditorDeviceCreate = "editor_device_create";
    public const string EditorDeviceRevoke = "editor_device_revoke";
    public const string DeviceRevoke = "device_revoke";
    public const string DeviceReset = "device_reset";
    public const string KeyExport = "key_export";
    public const string KeyImport = "key_import";





    public const string KeyRewrap = "key_rewrap";
    public const string Rollback = "rollback";
    public const string AutoRebase = "auto_rebase";
    public const string StateSaveFailed = "state_save_failed";
    public const string Error = "error";



    public const string AuditCleared = "audit_cleared";
}


public class SyncAuditEvent
{
    public string Ts { get; set; } = "";
    public string Ev { get; set; } = "";
    public string Server { get; set; } = "";
    public string Space { get; set; } = "";
    public string Device { get; set; } = "";
    public long Ver { get; set; }
    public string Detail { get; set; } = "";
    public bool Ok { get; set; } = true;
}

public static class SyncAuditLog
{

    public const long RotateBytes = 2 * 1024 * 1024;
    private const int MaxArchiveFiles = 9;

    private static readonly JsonlAuditStore<SyncAuditEvent> Store = new("sync-audit", RotateBytes, MaxArchiveFiles);


    public static void SetBaseDir(string? dir) => Store.SetBaseDir(dir);



    public static void Write(SyncAuditEvent evt)
    {
        if (evt == null) return;
        try
        {
            Prepare(evt);
            Store.Append(evt);
        }
        catch (Exception ex)
        {


            AuditWriteHealth.Report("sync-audit write", ex);
        }
    }





    public static void WriteOrdered(SyncAuditEvent evt)
    {
        if (evt == null) return;
        try
        {
            Prepare(evt);
            Store.AppendOrdered(evt);
        }
        catch (Exception ex)
        {
            AuditWriteHealth.Report("sync-audit write", ex);
        }
    }



    public static void ClearAllWithMarker()
    {
        try
        {
            var marker = new SyncAuditEvent { Ev = SyncAuditEvents.AuditCleared, Detail = "-", Ok = true };
            Prepare(marker);
            Store.ClearAllWithMarker(marker);
        }
        catch (Exception ex)
        {
            AuditWriteHealth.Report("sync-audit clear", ex);
        }
    }


    internal static void WaitForPendingWrites() => Store.WaitForPendingWrites();


    private static void Prepare(SyncAuditEvent evt)
    {
        if (string.IsNullOrEmpty(evt.Ts)) evt.Ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");



        evt.Ev = AuditSanitizer.Clean(evt.Ev);
        evt.Server = AuditSanitizer.Clean(evt.Server);
        evt.Space = AuditSanitizer.Clean(ShortId(evt.Space));
        evt.Device = AuditSanitizer.Clean(evt.Device);
        evt.Detail = AuditSanitizer.Clean(evt.Detail);
    }


    public static List<SyncAuditEvent> ReadLatest(int max) => Store.ReadLatest(max);



    internal static void ClearAll() => Store.ClearAll();


    public static int CountAll() => Store.CountAll();


    public static string ShortId(string? id)
        => string.IsNullOrEmpty(id) ? "" : id.Length <= 8 ? id : id.Substring(0, 8);
}
