





using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Novara.Models;

namespace Novara.Services;

public static class McpService
{
    public const string PipeName =
#if DEBUG
        "Novara.Mcp.Dev";
#else
        "Novara.Mcp";
#endif

    
    public static Func<string, bool>? AuthorizeClient;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static CancellationTokenSource? _cts;
    private static Thread? _thread;
    private static readonly object _gate = new();
    private static bool _started;

    public static void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }
        _cts = new CancellationTokenSource();
        _thread = new Thread(() => ServerLoop(_cts.Token)) { IsBackground = true, Name = "McpServer" };
        _thread.Start();
    }

    public static void Stop()
    {
        lock (_gate)
        {
            if (!_started) return;
            _started = false;
        }
        _cts?.Cancel();
        _cts = null;
    }

    private static void ServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                try { pipe.WaitForConnectionAsync(ct).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { break; }
                // NM5: back off on repeated failures - a contested/ACL-blocked pipe name must not
                // spin the server thread at full speed.
                catch { Thread.Sleep(500); continue; }
                
                
                
                var conn = pipe;
                pipe = null;
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try { HandleConnection(conn, ct); }
                    catch { }
                    finally { conn.Dispose(); }
                }, CancellationToken.None);
            }
            catch (OperationCanceledException) { break; }
            catch { Thread.Sleep(500); } // N4-41: pipe CONSTRUCTION failure (name held/ACL) - same NM5 backoff as the wait, no full-speed spin
            finally { pipe?.Dispose(); }
        }
    }

    private static void HandleConnection(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
            { AutoFlush = true, NewLine = "\n" };

            var helloLine = ReadLineWithTimeout(reader, TimeSpan.FromSeconds(10));
            if (helloLine == null) return; 
            var hello = JsonSerializer.Deserialize<HelloMsg>(helloLine, JsonOpts);
            var helloResp = Authorize(hello);
            writer.WriteLine(JsonSerializer.Serialize(helloResp, JsonOpts));
            if (!helloResp.Ok) return;

            
            var clientPath = (hello?.ClientPath ?? "").Trim();

            while (!ct.IsCancellationRequested)
            {
                var line = ReadLineWithTimeout(reader, TimeSpan.FromSeconds(60));
                if (line == null) break; 
                // NM14: one malformed line must not kill the whole session - skip and keep serving.
                try
                {
                    var req = JsonSerializer.Deserialize<ReqMsg>(line, JsonOpts);
                    if (req == null) continue;
                    var resp = Execute(req, clientPath);
                    writer.WriteLine(JsonSerializer.Serialize(resp, JsonOpts));
                }
                catch (System.Text.Json.JsonException)
                {
                    // N3-25: an unparseable line previously got NO response - the agent sat through
                    // the full 60s read timeout before erroring. Fail fast with an explicit error.
                    try
                    {
                        writer.WriteLine(JsonSerializer.Serialize(new RespMsg
                        {
                            Id = 0, Ok = false, // id unknowable on a parse failure (JSON-RPC convention)
                            Error = new RespError { Message = "请求不是合法 JSON" }
                        }, JsonOpts));
                    }
                    catch { }
                }
                catch (IOException) { throw; }
                catch { continue; }
            }
        }
        catch { }
    }

    
    private static string? ReadLineWithTimeout(StreamReader reader, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try { return reader.ReadLineAsync(cts.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return null; }
    }

    private static HelloResp Authorize(HelloMsg? hello)
    {
        var clientPath = (hello?.ClientPath ?? "").Trim();
        if (hello == null)
        {
            Audit("auth_denied", clientPath, ok: false, reason: "握手无效");
            return new HelloResp { Ok = false, Error = "握手无效" };
        }
        var store = App.Store;
        if (store == null || !store.IsLoaded)
        {
            Audit("auth_denied", clientPath, ok: false, reason: "数据库未解锁，请先在 Novara 中解锁");
            return new HelloResp { Ok = false, Error = "数据库未解锁，请先在 Novara 中解锁" };
        }

        var settings = store.Database.AppSettings;
        if (!settings.McpEnabled)
        {
            Audit("auth_denied", clientPath, ok: false, reason: "MCP 接口已关闭，请在 Novara 设置中开启");
            return new HelloResp { Ok = false, Error = "MCP 接口已关闭，请在 Novara 设置中开启" };
        }
        if (string.IsNullOrEmpty(settings.McpToken) || !FixedTimeEquals(hello.Token, settings.McpToken))
        {
            Audit("auth_denied", clientPath, ok: false, reason: "访问令牌无效，请到 Novara 设置页「MCP 接口」卡片复制最新配置");
            return new HelloResp { Ok = false, Error = "访问令牌无效，请到 Novara 设置页「MCP 接口」卡片复制最新配置" };
        }

        // NM2: concurrent first-authorizations raced on the non-thread-safe List; the gate also
        // serializes popup authorizations server-side (a second client waits its turn).
        
        // run inside the lock - holding WhitelistGate that long froze every UI reader (settings page
        
        // re-check + add under lock.
        bool known;
        lock (WhitelistGate)
        {
            known = clientPath.Length > 0 && store.Database.AppSettings.McpAllowedProcesses.Contains(clientPath);
        }
        if (!known)
        {
            bool allowed = AuthorizeClient?.Invoke(clientPath) ?? false;
            if (!allowed)
            {
                Audit("auth_denied", clientPath, ok: false, reason: "该进程尚未授权，请先打开 Novara 完成授权");
                return new HelloResp { Ok = false, Error = "该进程尚未授权，请先打开 Novara 完成授权" };
            }
            bool addedNow = false;
            lock (WhitelistGate)
            {
                if (clientPath.Length > 0 && !store.Database.AppSettings.McpAllowedProcesses.Contains(clientPath))
                {
                    store.Database.AppSettings.McpAllowedProcesses.Add(clientPath);
                    addedNow = true;
                }
                // 9.2#5: fine-grained record with the D1 default set - idempotent (the authorize
                // dialog may have pre-created it before the server-side write lands).
                if (clientPath.Length > 0)
                    McpPermissions.EnsureDefaultRecord(store.Database.AppSettings, clientPath);
            }
            _ = store.SaveAsync();
            if (addedNow) Audit("auth_new", clientPath); 
        }
        else
        {
            Audit("auth_ok", clientPath);
        }
        return new HelloResp { Ok = true };
    }

    /// <summary>Guards McpAllowedProcesses check+add across concurrent HandleConnection threads.</summary>
    private static readonly object WhitelistGate = new();

    /// <summary>N2-21: UI-side first-grant pre-seed must share WhitelistGate with the pipe threads'
    /// check+add (N4C-02 pattern) - an unlocked EnsureDefaultRecord raced a concurrent first-auth.</summary>
    public static void SeedDefaultPermission(string path)
    {
        lock (WhitelistGate)
        {
            var settings = App.Store?.Database.AppSettings;
            if (settings != null) McpPermissions.EnsureDefaultRecord(settings, path);
        }
    }

    /// <summary>N2-21: UI-side matrix save must share WhitelistGate - FirstOrDefault enumeration and
    /// Add on the live List raced the pipe threads' writes (List is not thread-safe).</summary>
    public static void SaveClientPermissions(string path, McpPerm bits)
    {
        lock (WhitelistGate)
        {
            var settings = App.Store?.Database.AppSettings;
            if (settings == null) return;
            var rec = settings.McpClientPermissions.FirstOrDefault(r => r.Path == path);
            if (rec != null) rec.Permissions = (long)bits;
            else settings.McpClientPermissions.Add(new Novara.Models.McpClientPermRecord { Path = path, Permissions = (long)bits });
        }
    }

    /// <summary>N2-21: UI-side matrix load shares WhitelistGate for the same reason (read-side gap).</summary>
    public static McpPerm ReadClientPermissions(string path)
    {
        lock (WhitelistGate)
        {
            var settings = App.Store?.Database.AppSettings;
            return (McpPerm)(settings?.McpClientPermissions.FirstOrDefault(r => r.Path == path)?.Permissions ?? 0L);
        }
    }

    /// <summary>N4C-02: UI-side revoke must share WhitelistGate with the pipe threads' check+add -
    /// an unlocked Remove racing a concurrent first-auth corrupted the List.</summary>
    public static bool RevokeAuthorizedProcess(string path)
    {
        lock (WhitelistGate)
        {
            var settings = App.Store?.Database.AppSettings;
            var removed = settings?.McpAllowedProcesses.Remove(path) ?? false;
            settings?.McpClientPermissions.RemoveAll(r => r.Path == path); // 9.2#5: fine-grained record dies with the grant (downgrade-compat list above stays in sync)
            if (removed) Audit("revoke", path); 
            return removed;
        }
    }

    /// <summary>N4C-02: snapshot for UI rendering - iterating the live List raced concurrent Adds.</summary>
    public static List<string> GetAuthorizedSnapshot()
    {
        lock (WhitelistGate)
        {
            var settings = App.Store?.Database.AppSettings;
            return settings?.McpAllowedProcesses.ToList() ?? new List<string>();
        }
    }

    
    private static readonly object ExecGate = new();

    private static RespMsg Execute(ReqMsg req, string clientPath)
    {
        var store = App.Store;
        if (store == null || !store.IsLoaded)
        {
            Audit("call", clientPath, req.Method, ok: false, reason: "数据库未解锁，请先在 Novara 中解锁");
            return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = "数据库未解锁，请先在 Novara 中解锁" } };
        }
        // N5C-02: the handshake gate is one-shot - re-check the master switch per call so toggling it
        // off (or revoking access) cuts established sessions immediately, matching delete_item semantics.
        if (!store.Database.AppSettings.McpEnabled)
        {
            Audit("call", clientPath, req.Method, ok: false, reason: "MCP 接口已关闭，请在 Novara 设置中开启");
            return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = "MCP 接口已关闭，请在 Novara 设置中开启" } };
        }
        if (store.IsSaveSuppressed)
        {
            Audit("call", clientPath, req.Method, ok: false, reason: "已恢复备份数据，请重启 Novara 后再操作"); // N5S-08
            return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = "已恢复备份数据，请重启 Novara 后再操作" } };
        }

        bool isWrite = req.Method.StartsWith("create_", StringComparison.Ordinal)
                    || req.Method.StartsWith("update_", StringComparison.Ordinal)
                    || req.Method == "delete_item";

        
        
        string? readType = null;
        if (req.Method == "read_item")
        {
            try { readType = CheckType(GetStrStrict(req.Params, "type") ?? throw new McpError("缺少参数 type")); }
            catch (McpError ex)
            {
                Audit("call", clientPath, req.Method, ok: false, reason: ex.Message);
                return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = ex.Message } };
            }
        }
        // 9.2#5: per-client fine-grained permission gate. Unknown methods map to None (no
        // requirement) and fall through to the "unknown tool" error below. delete_item additionally
        // needs the global McpDeleteEnabled master switch (checked inside Delete) - client bit AND switch.
        var required = McpPermissions.RequiredFor(req.Method, req.Method == "read_item" ? readType : GetStr(req.Params, "type"));
        var granted = McpPermissions.GetFor(store.Database.AppSettings, clientPath);
        if ((granted & required) != required)
        {
            var denyReason = $"权限不足：需要 {McpPermissions.Describe(required)}";
            Audit("call", clientPath, req.Method, ok: false, reason: denyReason);
            return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = "权限不足，请在 Novara 设置的 MCP 授权面板中调整该客户端的权限" } };
        }

        var target = "-";
        var title = "";
        Action? postGate = null; // N4-43: slow side-effect IO staged by delete_item, run after the gate
        // N2-68: groupId presence must be known BEFORE the dispatch - an explicitly EMPTY value means
        // "move to uncategorized" while absence means "leave the group untouched".
        var grpRaw = GetStr(req.Params, "groupId");
        var clearGroup = grpRaw != null && grpRaw.Trim().Length == 0;
        // N2-69: MCP-created entries land in the ACTIVE workspace (parity with UI creation).
        var currentWs = App.CurrentWorkspaceId;
        try
        {
            object? result;
            lock (ExecGate)
            {
                // N3-05: re-validate inside the gate. RelockStore / backup-restore may invalidate or
                // swap this store between the entry checks above and this lock - without the re-check
                // a) store.Database is null and the NRE (thrown outside any catch) leaves the request
                // unanswered, b) a stale decrypted db captured before the invalidation could serve
                // data after the lock. ReferenceEquals catches the swapped-fresh-instance case;
                // IsLoaded covers in-place invalidation. (Fully closing the residual two-statement
                
                if (!ReferenceEquals(App.Store, store) || !store.IsLoaded || store.Database == null)
                    throw new McpError("数据库已锁定，请先在 Novara 中解锁");
                // N4-42: the McpEnabled re-check at method entry sat outside this gate - a toggle-off
                // racing an in-flight call could pass the entry check and serve from a just-disabled
                // session. Same re-validation parity as the N3-05 store check above.
                if (!store.Database.AppSettings.McpEnabled)
                    throw new McpError("MCP 接口已关闭，请在 Novara 设置中开启");
                var db = store.Database;
                (target, title) = CaptureTarget(db, req); 
                result = req.Method switch
            {
                "list_items" => McpLogic.ListItems(db, CheckType(GetStrStrict(req.Params, "type"))),
                "read_item" => McpLogic.ReadItem(db, readType!, ReqStr(req.Params, "id")), // N4-44: type validated strictly before the gate
                "search_items" => McpLogic.SearchItems(db, ReqStr(req.Params, "query"), CheckType(GetStrStrict(req.Params, "type"))),
                "delete_item" => Delete(store, ReqStr(req.Params, "type"), ReqStr(req.Params, "id"), ref postGate),

                "create_memo" => McpLogic.CreateMemo(db, ReqStr(req.Params, "name"), ReqStr(req.Params, "type"),
                    GetStr(req.Params, "keyInfo"), GetFields(req.Params), GetGuid(req.Params, "groupId"), GetStr(req.Params, "iconKey"), currentWs),
                "create_todo" => McpLogic.CreateTodo(db, ReqStr(req.Params, "title"), ReqStr(req.Params, "mainText"),
                    GetStrings(req.Params, "subTexts"), GetStr(req.Params, "iconKey"), currentWs),
                "create_note" => McpLogic.CreateNote(db, ReqStr(req.Params, "title"), ReqStr(req.Params, "content"), GetStr(req.Params, "iconKey"), currentWs),
                "create_diary" => CreateDiary(store, ReqStr(req.Params, "title"), ReqStr(req.Params, "content"), GetStr(req.Params, "format"), currentWs),
                "create_path" => McpLogic.CreatePath(db, ReqStr(req.Params, "name"), ReqStr(req.Params, "path"), GetStr(req.Params, "note"), currentWs),

                "update_memo" => UpdateMemo(db, ReqStr(req.Params, "id"), GetStr(req.Params, "name"), GetStr(req.Params, "type"),
                    GetStr(req.Params, "keyInfo"), GetFields(req.Params), clearGroup ? (Guid?)null : GetGuid(req.Params, "groupId"), GetStr(req.Params, "iconKey"), clearGroup),
                "update_todo" => UpdateTodo(db, ReqStr(req.Params, "id"), GetStr(req.Params, "title"), GetStr(req.Params, "mainText"),
                    GetStrings(req.Params, "subTexts"), GetStr(req.Params, "iconKey")),
                "update_note" => UpdateNote(db, ReqStr(req.Params, "id"), GetStr(req.Params, "title"), GetStr(req.Params, "content"), GetStr(req.Params, "iconKey")),
                "update_diary" => UpdateDiary(db, ReqStr(req.Params, "id"), GetStr(req.Params, "title"), GetStr(req.Params, "content")),
                "update_path" => UpdatePath(db, ReqStr(req.Params, "id"), GetStr(req.Params, "name"), GetStr(req.Params, "path"), GetStr(req.Params, "note")),

                _ => throw new McpError($"未知工具: {req.Method}")
                };
            }
            postGate?.Invoke(); // N4-43: slow IO (schtasks/file) runs outside the ExecGate critical section
            if (isWrite)
            {
                _ = store.SaveAsync();
                App.MainWindow?.NotifyExternalDbMutation(); // N2-01: cached tab pages must learn about direct db writes (marshals to UI thread internally)
            }
            Audit("call", clientPath, req.Method, target, title, isWrite, true);
            return new RespMsg { Id = req.Id, Ok = true, Result = result };
        }
        catch (McpError ex)
        {
            Audit("call", clientPath, req.Method, target, title, isWrite, false, ex.Message);
            return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = ex.Message } };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"McpService 执行失败: {ex}");
            Audit("call", clientPath, req.Method, target, title, isWrite, false, "内部错误");
            return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = "内部错误" } };
        }
    }

    
    private static (string Target, string Title) CaptureTarget(NovaraDatabase db, ReqMsg req)
    {
        try
        {
            var m = req.Method;
            if (m == "list_items")
                return ($"list:{GetStr(req.Params, "type") ?? "all"}", "");
            if (m == "search_items")
                return ($"search:{GetStr(req.Params, "type") ?? "all"}", McpAuditLog.TruncateTitle(GetStr(req.Params, "query")));

            switch (m)
            {
                case "create_memo": return ("memo:new", McpAuditLog.TruncateTitle(GetStr(req.Params, "name")));
                case "create_todo":
                case "create_note":
                case "create_diary": return ($"{InferType(m)}:new", McpAuditLog.TruncateTitle(GetStr(req.Params, "title")));
                case "create_path": return ("path:new", McpAuditLog.TruncateTitle(GetStr(req.Params, "name")));
            }

            var type = GetStr(req.Params, "type") ?? InferType(m);
            var id = GetStr(req.Params, "id") ?? "";
            if (id.Length > 0 && Guid.TryParse(id, out _))
                return ($"{type}:{IdShort(id)}", McpAuditLog.TruncateTitle(FindTitle(db, type, id)));
            return ($"{type}:-", "");
        }
        catch { return ("-", ""); }
    }

    private static string FindTitle(NovaraDatabase db, string type, string id)
    {
        return type switch
        {
            "memo" => db.MemoEntries.FirstOrDefault(x => x.Id.ToString() == id)?.Name ?? "",
            "path" => db.PathBackupItems.FirstOrDefault(x => x.Id.ToString() == id)?.Name ?? "",
            "todo" => db.TodoCards.FirstOrDefault(x => x.Id.ToString() == id)?.Title ?? "",
            "note" => db.NoteCards.FirstOrDefault(x => x.Id.ToString() == id)?.Title ?? "",
            "diary" => db.DiaryItems.FirstOrDefault(x => x.Id.ToString() == id)?.Title ?? "",
            _ => ""
        };
    }

    private static string InferType(string method)
        => method.EndsWith("_memo", StringComparison.Ordinal) ? "memo"
         : method.EndsWith("_todo", StringComparison.Ordinal) ? "todo"
         : method.EndsWith("_note", StringComparison.Ordinal) ? "note"
         : method.Contains("diary", StringComparison.Ordinal) ? "diary"
         : method.Contains("path", StringComparison.Ordinal) ? "path"
         : "-";

    private static string IdShort(string id) => id.Length <= 8 ? id : id.Substring(0, 8);

    
    private static void Audit(string ev, string path, string tool = "", string target = "", string title = "",
        bool write = false, bool ok = true, string reason = "")
    {
        try
        {
            var evt = new McpAuditEvent
            {
                Ev = ev,
                Path = path ?? "",
                Tool = tool ?? "",
                Target = target ?? "",
                Title = title ?? "",
                Write = write,
                Ok = ok,
                Reason = reason ?? ""
            };
            
            System.Threading.Tasks.Task.Run(() => McpAuditLog.Write(evt));
        }
        catch { }
    }

    

    private static string CreateDiary(NovaraStore store, string title, string content, string? format, string? workspaceId)
    {
        var fmt = string.IsNullOrWhiteSpace(format) ? "markdown" : format.Trim().ToLowerInvariant();
        if (fmt == "html") content = HtmlSanitizer.Sanitize(content);
        return McpLogic.CreateDiary(store.Database, title, content, fmt, workspaceId);
    }

    private static object UpdateDiary(NovaraDatabase db, string id, string? title, string? content)
    {
        if (content != null)
        {
            var e = db.DiaryItems.FirstOrDefault(x => x.Id == id && !x.IsDeleted);
            if (e != null && e.Format == "html") content = HtmlSanitizer.Sanitize(content);
        }
        McpLogic.UpdateDiary(db, id, title, content);
        return true;
    }

    private static object UpdateMemo(NovaraDatabase db, string id, string? name, string? type, string? keyInfo,
        List<McpFieldInput>? fields, Guid? groupId, string? iconKey, bool clearGroupId)
    {
        McpLogic.UpdateMemo(db, id, name, type, keyInfo, fields, groupId, iconKey, clearGroupId);
        return true;
    }

    private static object UpdateTodo(NovaraDatabase db, string id, string? title, string? mainText, List<string>? subTexts, string? iconKey)
    {
        McpLogic.UpdateTodo(db, id, title, mainText, subTexts, iconKey);
        return true;
    }

    private static object UpdateNote(NovaraDatabase db, string id, string? title, string? content, string? iconKey)
    {
        McpLogic.UpdateNote(db, id, title, content, iconKey);
        return true;
    }

    private static object UpdatePath(NovaraDatabase db, string id, string? name, string? path, string? note)
    {
        McpLogic.UpdatePath(db, id, name, path, note);
        return true;
    }

    // N4-43: slow side-effect IO (schtasks cancel, stickies.json write) must not run inside the
    // ExecGate critical section - it blocked every concurrent MCP session. Delete now stages the
    // post-gate work in `postGate`; Execute invokes it right after the lock releases.
    private static object Delete(NovaraStore store, string type, string id, ref Action? postGate)
    {
        if (!store.Database.AppSettings.McpDeleteEnabled)
            throw new McpError("MCP 删除操作未开启，请在 Novara 设置中打开权限");
        McpLogic.DeleteItem(store.Database, type, id);
        // N2-08: a UI soft-delete carries side effects a bare IsDeleted flip misses (N4P-04 trio) -
        // the schtasks one-shot reminder would still fire with no card to show, and a desktop sticky
        // would keep displaying the deleted entry for up to 7 days. Mirror the PlanPage teardown.
        if ((type == "todo" || type == "note") && Guid.TryParse(id, out var delId))
        {
            var card = type == "todo"
                ? store.Database.TodoCards.FirstOrDefault(x => x.Id == delId) as object
                : store.Database.NoteCards.FirstOrDefault(x => x.Id == delId);
            switch (card)
            {
                case Models.TodoCard t when t.ReminderAt != null:
                    t.ReminderAt = null; t.ReminderSetAt = null;
                    postGate += () => Services.ReminderScheduler.Cancel(delId);
                    break;
                case Models.NoteCard n when n.ReminderAt != null:
                    n.ReminderAt = null; n.ReminderSetAt = null;
                    postGate += () => Services.ReminderScheduler.Cancel(delId);
                    break;
            }
            postGate += () => Services.StickySync.RemoveNote(id);
        }
        return true;
    }

    

    private static string? GetStr(JsonElement? p, string key)
        => p is { } e && e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    // N3-24: for keys where null means "default" (e.g. list/search type = all) a PRESENT-but-non-string
    // value must not silently degrade to null -> all (an agent passing type: 123 would get every
    // partition back instead of an error). Strict variant: absent -> null, string -> value, else error.
    private static string? GetStrStrict(JsonElement? p, string key)
    {
        if (p is not { } e || e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind != JsonValueKind.String) throw new McpError($"{key} 必须是字符串");
        return v.GetString();
    }

    private static string ReqStr(JsonElement? p, string key)
        => GetStr(p, key) ?? throw new McpError($"缺少参数 {key}");

    private static string? CheckType(string? type)
        => type == null ? null : (McpLogic.IsValidType(type) ? type : throw new McpError($"未知类型: {type}"));

    private static Guid? GetGuid(JsonElement? p, string key)
    {
        // NM16: a present-but-unparseable GUID is an agent error - say so instead of silently
        // filing the entry under uncategorized while the agent believes grouping succeeded.
        var s = GetStr(p, key);
        if (string.IsNullOrEmpty(s)) return null;
        if (!Guid.TryParse(s, out var g)) throw new McpError($"{key} 不是有效的 GUID: {s}");
        return g;
    }

    private static List<McpFieldInput>? GetFields(JsonElement? p)
    {
        if (p is not { } e || !e.TryGetProperty("fields", out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var list = new List<McpFieldInput>();
        foreach (var x in v.EnumerateArray())
        {
            if (x.ValueKind != JsonValueKind.Object)
                throw new McpError("fields: 每个元素必须是对象 {label, value, canCopy}"); 
            list.Add(new McpFieldInput
            {
                Label = x.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : "",
                Value = x.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String ? val.GetString() ?? "" : "",
                CanCopy = x.TryGetProperty("canCopy", out var c) && c.ValueKind == JsonValueKind.True
            });
        }
        return list;
    }

    private static List<string>? GetStrings(JsonElement? p, string key)
    {
        if (p is not { } e || !e.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        // N4-45: strict element check (N3-24 philosophy) - a non-string element used to be silently
        // dropped ("a",123,"b" -> "a","b"), leaving the agent unaware its payload got mangled.
        foreach (var x in v.EnumerateArray())
            if (x.ValueKind != JsonValueKind.String) throw new McpError($"{key} 必须是字符串数组");
        return v.EnumerateArray().Select(x => x.GetString()!).ToList();
    }

    private static bool FixedTimeEquals(string? a, string b)
    {
        if (a == null) return false;
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    

    private class HelloMsg
    {
        public string Type { get; set; } = "";
        public string Token { get; set; } = "";
        public string ClientPath { get; set; } = "";
    }

    private class HelloResp
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
    }

    private class ReqMsg
    {
        public long Id { get; set; }
        public string Method { get; set; } = "";
        public JsonElement? Params { get; set; }
    }

    private class RespMsg
    {
        public long Id { get; set; }
        public bool Ok { get; set; }
        public object? Result { get; set; }
        public RespError? Error { get; set; }
    }

    private class RespError
    {
        public string Message { get; set; } = "";
    }
}
