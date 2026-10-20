using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Novara.Models;

namespace Novara.Services;

public static class McpService
{



    public static string PipeName => Novara.Ipc.McpIpcName.Current;


    public static Func<string, bool>? AuthorizeClient;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly object _gate = new();
    private static bool _started;

    public static void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }


        var cts = new CancellationTokenSource();
        new Thread(() => ServerLoop(cts.Token)) { IsBackground = true, Name = "McpServer" }.Start();
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
            catch { Thread.Sleep(500); }
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


            var lineReader = new CappedLineReader(reader);

            var helloLine = ReadLineWithTimeout(lineReader, TimeSpan.FromSeconds(10), HandshakeMaxLineChars, out var helloTooLong);
            if (helloTooLong)
            {
                try { writer.WriteLine(JsonSerializer.Serialize(new HelloResp { Ok = false, Error = "握手内容过长" }, JsonOpts)); } catch { }
                return;
            }
            if (helloLine == null) return;
            var hello = JsonSerializer.Deserialize<HelloMsg>(helloLine, JsonOpts);



            var identity = McpClientIdentity.ResolveAgent(pipe);
            var helloResp = Authorize(hello, identity, out var canonicalPath);
            writer.WriteLine(JsonSerializer.Serialize(helloResp, JsonOpts));
            if (!helloResp.Ok) return;




            var clientPath = canonicalPath;



            var handshakeToken = hello?.Token ?? "";

            while (!ct.IsCancellationRequested)
            {
                var line = ReadLineWithTimeout(lineReader, TimeSpan.FromSeconds(60), RequestMaxLineChars, out var lineTooLong);
                if (lineTooLong)
                {

                    try
                    {
                        writer.WriteLine(JsonSerializer.Serialize(new RespMsg
                        {
                            Id = 0, Ok = false,
                            Error = new RespError { Message = "请求过长" }
                        }, JsonOpts));
                    }
                    catch { }
                    break;
                }
                if (line == null) break;

                try
                {
                    var req = JsonSerializer.Deserialize<ReqMsg>(line, JsonOpts);
                    if (req == null) { System.Diagnostics.Debug.WriteLine("null/blank line from a pipe client (no reply, not audited - a direct-connect client could flood the user-visible list)"); continue; }
                    var resp = Execute(req, clientPath, handshakeToken);
                    writer.WriteLine(JsonSerializer.Serialize(resp, JsonOpts));
                }
                catch (System.Text.Json.JsonException)
                {


                    try
                    {
                        writer.WriteLine(JsonSerializer.Serialize(new RespMsg
                        {
                            Id = 0, Ok = false,
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



    private const int HandshakeMaxLineChars = 8 * 1024;
    private const int RequestMaxLineChars = 4 * 1024 * 1024;

    private static string? ReadLineWithTimeout(CappedLineReader reader, TimeSpan timeout, int maxChars, out bool tooLong)
    {
        tooLong = false;
        using var cts = new CancellationTokenSource(timeout);
        try { return reader.ReadLineAsync(maxChars, cts.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return null; }
        catch (System.IO.InvalidDataException) { tooLong = true; return null; }
    }



    private sealed class CappedLineReader
    {
        private readonly StreamReader _reader;
        private readonly char[] _buffer = new char[4096];
        private int _pos;
        private int _len;

        internal CappedLineReader(StreamReader reader) => _reader = reader;

        internal async Task<string?> ReadLineAsync(int maxChars, CancellationToken ct)
        {
            var line = new StringBuilder();
            while (true)
            {
                if (_pos >= _len)
                {
                    _len = await _reader.ReadAsync(_buffer.AsMemory(0, _buffer.Length), ct).ConfigureAwait(false);
                    _pos = 0;
                    if (_len == 0) return line.Length == 0 ? null : line.ToString();
                }
                var start = _pos;
                while (_pos < _len && _buffer[_pos] != '\n') _pos++;
                var count = _pos - start;
                if (line.Length + count > maxChars) throw new System.IO.InvalidDataException("line exceeds the allowed length");
                line.Append(_buffer, start, count);
                if (_pos < _len && _buffer[_pos] == '\n')
                {
                    _pos++;
                    if (line.Length > 0 && line[^1] == '\r') line.Length--;
                    return line.ToString();
                }
            }
        }
    }

    private static HelloResp Authorize(HelloMsg? hello, McpClientIdentity.AgentIdentity identity, out string canonicalPath)
    {

        var reported = (hello?.ClientPath ?? "").Trim();
        var clientPath = identity.ImagePath.Trim();




        canonicalPath = clientPath;
        if (hello == null)
        {
            Audit("auth_denied", reported, ok: false, reason: "握手无效");
            return new HelloResp { Ok = false, Error = "握手无效" };
        }





        if (clientPath.Length == 0)
        {
            var why = identity.FailureReason.Length > 0 ? identity.FailureReason : "无法确认客户端进程身份";
            Audit("auth_denied", reported, ok: false, reason: why);
            return new HelloResp
            {
                Ok = false,
                Error = why + "。请让 Agent 客户端保持运行并由它直接启动 NovaraMCP"
                    + "（不要经一次性启动器或包装脚本），然后重试。",
            };
        }
        var identityMismatch = reported.Length > 0 && !string.Equals(reported, clientPath, StringComparison.OrdinalIgnoreCase);
        var store = App.Store;
        if (store == null || !store.IsLoaded)
        {
            Audit("auth_denied", clientPath, ok: false, reason: "数据库未解锁，请先在 Novara 中解锁");
            return new HelloResp { Ok = false, Error = "数据库未解锁，请先在 Novara 中解锁" };
        }




        var db = store.Database;
        if (db == null)
        {
            Audit("auth_denied", clientPath, ok: false, reason: "数据库未解锁，请先在 Novara 中解锁");
            return new HelloResp { Ok = false, Error = "数据库未解锁，请先在 Novara 中解锁" };
        }

        var settings = db.AppSettings;
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







        bool known;
        lock (WhitelistGate)
        {
            known = clientPath.Length > 0 && db.AppSettings.McpAllowedProcesses.Contains(clientPath);



            if (!known && clientPath.Length > 0)
            {
                var existing = db.AppSettings.McpAllowedProcesses
                    .FirstOrDefault(p => string.Equals(p, clientPath, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(existing)) { clientPath = existing; known = true; }
            }
        }

        canonicalPath = clientPath;
        if (identityMismatch)
            System.Diagnostics.Debug.WriteLine($"MCP 客户端自报身份与系统解析不一致：reported={reported} resolved={clientPath}");
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
                if (clientPath.Length > 0 && !db.AppSettings.McpAllowedProcesses.Contains(clientPath))
                {
                    db.AppSettings.McpAllowedProcesses.Add(clientPath);
                    addedNow = true;
                }


                if (clientPath.Length > 0)
                    McpPermissions.EnsureDefaultRecord(db.AppSettings, clientPath);
            }
            _ = store.SaveAsync();


            if (addedNow) Audit("auth_new", clientPath, reason: identityMismatch ? $"自报身份与系统解析不一致：{reported}" : "");
        }
        else
        {

            Audit("auth_ok", clientPath, reason: identityMismatch ? $"自报身份与系统解析不一致：{reported}" : "");
        }
        return new HelloResp { Ok = true };
    }


    private static readonly object WhitelistGate = new();




    private static readonly object AuthDeniedThrottleGate = new();
    private static DateTimeOffset _lastAuthDeniedWrite = DateTimeOffset.MinValue;
    private static int _suppressedAuthDenied;
    private static readonly TimeSpan AuthDeniedThrottleWindow = TimeSpan.FromMinutes(1);



    public static void SeedDefaultPermission(string path)
    {
        lock (WhitelistGate)
        {


            var db = App.Store?.Database;
            if (db != null) McpPermissions.EnsureDefaultRecord(db.AppSettings, path);
        }
    }







    public static void SaveClientPermissions(string path, McpPerm bits)
    {
        lock (WhitelistGate)
        {
            var db = App.Store?.Database;
            if (db == null) return;
            var settings = db.AppSettings;


            var rec = settings.McpClientPermissions.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            bits = McpPermissions.ApplyDeleteGate(bits, (McpPerm)(rec?.Permissions ?? 0L), !settings.McpDeleteEnabled);
            if (rec != null) rec.Permissions = (long)bits;
            else settings.McpClientPermissions.Add(new Novara.Models.McpClientPermRecord { Path = path, Permissions = (long)bits });
        }
    }


    public static McpPerm ReadClientPermissions(string path)
    {
        lock (WhitelistGate)
        {
            var db = App.Store?.Database;
            var settings = db?.AppSettings;
            return (McpPerm)(settings?.McpClientPermissions
                .FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))?.Permissions ?? 0L);
        }
    }



    public static bool RevokeAuthorizedProcess(string path)
    {
        lock (WhitelistGate)
        {
            var db = App.Store?.Database;
            if (db == null) return false;
            var settings = db.AppSettings;




            var list = settings?.McpAllowedProcesses;
            var removed = (list?.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) ?? 0) > 0;
            removed |= (settings?.McpClientPermissions.RemoveAll(
                            r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)) ?? 0) > 0;
            if (removed) Audit("revoke", path);
            return removed;
        }
    }



    public static void RecordTokenReset()
        => Audit("token_reset", "", reason: "MCP access token was regenerated; existing sessions are no longer valid");

    public static void RecordPermissionChange(string clientPath, string detail)
        => Audit("perm_change", clientPath, reason: detail);




    public static void RecordMasterSwitch(bool deleteSwitch, bool enabled)
        => Audit(deleteSwitch ? "delete_switch" : "master_switch", "", reason: enabled ? "switched on" : "switched off");


    public static List<string> GetAuthorizedSnapshot()
    {
        lock (WhitelistGate)
        {
            var settings = App.Store?.Database?.AppSettings;
            return settings?.McpAllowedProcesses.ToList() ?? new List<string>();
        }
    }


    private static readonly object ExecGate = new();

    private static RespMsg Execute(ReqMsg req, string clientPath, string handshakeToken)
    {
        req.Method ??= "";
        var store = App.Store;
        if (store == null || !store.IsLoaded)
        {
            Audit("call", clientPath, req.Method, ok: false, reason: "数据库未解锁，请先在 Novara 中解锁");
            return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = "数据库未解锁，请先在 Novara 中解锁" } };
        }




        var entryDb = store.Database;
        if (entryDb == null)
        {
            Audit("call", clientPath, req.Method, ok: false, reason: "数据库未解锁，请先在 Novara 中解锁");
            return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = "数据库未解锁，请先在 Novara 中解锁" } };
        }


        if (!entryDb.AppSettings.McpEnabled)
        {
            Audit("call", clientPath, req.Method, ok: false, reason: "MCP 接口已关闭，请在 Novara 设置中开启");
            return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = "MCP 接口已关闭，请在 Novara 设置中开启" } };
        }



        var currentToken = entryDb.AppSettings.McpToken;
        if (string.IsNullOrEmpty(currentToken) || !FixedTimeEquals(handshakeToken, currentToken))
        {
            Audit("call", clientPath, req.Method, ok: false, reason: "访问令牌已重置，请用最新配置重新连接");
            return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = "访问令牌已重置，请用最新配置重新连接" } };
        }
        if (store.IsSaveSuppressed)
        {
            Audit("call", clientPath, req.Method, ok: false, reason: "已恢复备份数据，请重启 Novara 后再操作");
            return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = "已恢复备份数据，请重启 Novara 后再操作" } };
        }

        bool isWrite = req.Method.StartsWith("create_", StringComparison.Ordinal)
                    || req.Method.StartsWith("update_", StringComparison.Ordinal)
                    || req.Method == "delete_item";






        string? validatedType = null;
        if (req.Method is "read_item" or "list_items" or "search_items")
        {
            try
            {
                var strictType = GetStrStrict(req.Params, "type");
                if (req.Method == "read_item") validatedType = CheckType(strictType ?? throw new McpError("缺少参数 type"));
                else if (strictType != null) validatedType = CheckType(strictType);
            }
            catch (McpError ex)
            {
                Audit("call", clientPath, req.Method, ok: false, reason: ex.Message);
                return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = ex.Message } };
            }
        }




        var required = McpPermissions.RequiredFor(req.Method, req.Method is "read_item" or "list_items" or "search_items"
            ? validatedType
            : GetStr(req.Params, "type"));
        var granted = McpPermissions.GetFor(entryDb.AppSettings, clientPath);
        if ((granted & required) != required)
        {
            var denyReason = $"权限不足：需要 {McpPermissions.Describe(required)}";
            Audit("call", clientPath, req.Method, ok: false, reason: denyReason);
            return new RespMsg { Id = req.Id, Ok = false, Error = new RespError { Message = "权限不足，请在 Novara 设置的 MCP 授权面板中调整该客户端的权限" } };
        }

        var target = "-";
        var title = "";
        Action? postGate = null;


        var grpRaw = GetStr(req.Params, "groupId");
        var clearGroup = grpRaw != null && grpRaw.Trim().Length == 0;





        try
        {
            object? result;
            lock (ExecGate)
            {







                if (!ReferenceEquals(App.Store, store) || !store.IsLoaded || store.Database == null)
                    throw new McpError("数据库已锁定，请先在 Novara 中解锁");



                if (!store.Database.AppSettings.McpEnabled)
                    throw new McpError("MCP 接口已关闭，请在 Novara 设置中开启");





                if (store.IsSaveSuppressed)
                    throw new McpError("已恢复备份数据，请重启 Novara 后再操作");
                var currentWs = App.CurrentWorkspaceId;
                var db = store.Database;
                (target, title) = CaptureTarget(db, req);
                result = req.Method switch
            {


                "list_items" => McpLogic.ListItems(db, validatedType),
                "read_item" => McpLogic.ReadItem(db, validatedType!, ReqStr(req.Params, "id")),
                "search_items" => McpLogic.SearchItems(db, ReqStr(req.Params, "query"), validatedType),
                "delete_item" => Delete(store, ReqStr(req.Params, "type"), ReqStr(req.Params, "id"), ref postGate),

                "create_memo" => McpLogic.CreateMemo(db, ReqStr(req.Params, "name"), ReqStr(req.Params, "type"),
                    GetStr(req.Params, "keyInfo"), GetFields(req.Params), GetGuid(req.Params, "groupId"), GetStr(req.Params, "iconKey"), currentWs),
                "create_todo" => McpLogic.CreateTodo(db, ReqStr(req.Params, "title"), ReqStr(req.Params, "mainText"),
                    GetStrings(req.Params, "subTexts"), GetStr(req.Params, "iconKey"), currentWs),
                "create_note" => McpLogic.CreateNote(db, ReqStr(req.Params, "title"), ReqStr(req.Params, "content"), GetStr(req.Params, "iconKey"), currentWs),
                "create_diary" => CreateDiary(db, ReqStr(req.Params, "title"), ReqStr(req.Params, "content"), GetStr(req.Params, "format"), currentWs),
                "create_path" => McpLogic.CreatePath(db, ReqStr(req.Params, "name"), ReqStr(req.Params, "path"), GetStr(req.Params, "note"), currentWs),

                "update_memo" => UpdateMemo(db, ReqStr(req.Params, "id"), GetStr(req.Params, "name"), GetStr(req.Params, "type"),
                    GetStr(req.Params, "keyInfo"), GetFields(req.Params), clearGroup ? (Guid?)null : GetGuid(req.Params, "groupId"), GetStr(req.Params, "iconKey"), clearGroup),
                "update_todo" => UpdateTodo(db, ReqStr(req.Params, "id"), GetStr(req.Params, "title"), GetStr(req.Params, "mainText"),
                    GetStrings(req.Params, "subTexts"), GetStr(req.Params, "iconKey"), ref postGate),
                "update_note" => UpdateNote(db, ReqStr(req.Params, "id"), GetStr(req.Params, "title"), GetStr(req.Params, "content"), GetStr(req.Params, "iconKey"), ref postGate),
                "update_diary" => UpdateDiary(db, ReqStr(req.Params, "id"), GetStr(req.Params, "title"), GetStr(req.Params, "content")),
                "update_path" => UpdatePath(db, ReqStr(req.Params, "id"), GetStr(req.Params, "name"), GetStr(req.Params, "path"), GetStr(req.Params, "note")),

                _ => throw new McpError($"未知工具: {req.Method}")
                };
            }





            try { postGate?.Invoke(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"McpService postGate 失败（已忽略，主操作已成功）: {ex}"); }
            if (isWrite)
            {
                _ = store.SaveAsync();
                App.MainWindow?.NotifyExternalDbMutation();
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










            if (m.StartsWith("update_") || !McpLogic.IsValidType(type))
                type = InferType(m);
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

            if (ev == "auth_denied")
            {
                lock (AuthDeniedThrottleGate)
                {
                    var now = DateTimeOffset.UtcNow;
                    if (now - _lastAuthDeniedWrite < AuthDeniedThrottleWindow)
                    {
                        _suppressedAuthDenied++;
                        return;
                    }
                    if (_suppressedAuthDenied > 0)
                        reason = $"{reason}（此前 {_suppressedAuthDenied} 条同类拒绝已合并）";
                    _suppressedAuthDenied = 0;
                    _lastAuthDeniedWrite = now;
                }
            }
            var evt = new McpAuditEvent
            {
                Ev = ev,



                Ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Path = path ?? "",
                Tool = tool ?? "",
                Target = target ?? "",
                Title = title ?? "",
                Write = write,
                Ok = ok,
                Reason = reason ?? ""
            };

            McpAuditLog.WriteOrdered(evt);
        }
        catch { }
    }





    private static string CreateDiary(NovaraDatabase db, string title, string content, string? format, string? workspaceId)
    {
        var fmt = string.IsNullOrWhiteSpace(format) ? "markdown" : format.Trim().ToLowerInvariant();
        if (fmt == "html") content = HtmlSanitizer.Sanitize(content);
        return McpLogic.CreateDiary(db, title, content, fmt, workspaceId);
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

    private static object UpdateTodo(NovaraDatabase db, string id, string? title, string? mainText, List<string>? subTexts, string? iconKey, ref Action? postGate)
    {
        McpLogic.UpdateTodo(db, id, title, mainText, subTexts, iconKey);



        if (Guid.TryParse(id, out var gid) && db.TodoCards.FirstOrDefault(x => x.Id == gid) is { } todo)
        {
            var items = BuildStickyTodoItems(todo.MainText ?? "", todo.SubTexts, todo.CheckedStates);
            postGate += () => RunStickyOnUi(() =>
            {
                if (Services.StickySync.Contains(id))
                    Services.StickySync.UpdateNote(id, "todo", todo.Title ?? "", "", items);
            });
        }
        return true;
    }

    private static object UpdateNote(NovaraDatabase db, string id, string? title, string? content, string? iconKey, ref Action? postGate)
    {
        McpLogic.UpdateNote(db, id, title, content, iconKey);
        if (Guid.TryParse(id, out var gid) && db.NoteCards.FirstOrDefault(x => x.Id == gid) is { } note)
        {
            var finalTitle = note.Title ?? "";
            var finalContent = note.Content ?? "";
            postGate += () => RunStickyOnUi(() =>
            {
                if (Services.StickySync.Contains(id))
                    Services.StickySync.UpdateNote(id, "note", finalTitle, finalContent);
            });
        }
        return true;
    }








    private static void RunStickyOnUi(Action action)
    {
        var dq = App.MainWindow?.DispatcherQueue;
        if (dq is null) { action(); return; }
        using var done = new System.Threading.ManualResetEventSlim(false);
        if (dq.TryEnqueue(() => { try { action(); } finally { done.Set(); } }))
        {
            if (!done.Wait(TimeSpan.FromSeconds(5)))
                System.Diagnostics.Debug.WriteLine("便签同步投递 UI 线程 5s 未完成（放弃等待，写盘可能稍后仍会落地）");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("便签同步投递 UI 线程失败（DispatcherQueue 拒绝入队）");
        }
    }


    private static List<Services.StickyTodoItem> BuildStickyTodoItems(string mainText, List<string>? subTexts, List<bool>? states)
    {
        var items = new List<Services.StickyTodoItem>
        {
            new() { Label = mainText ?? "", Checked = states is { Count: > 0 } && states[0] }
        };
        if (subTexts != null)
            for (int i = 0; i < subTexts.Count; i++)
                items.Add(new() { Label = subTexts[i], Checked = states is { Count: > 0 } && i + 1 < states.Count && states[i + 1] });
        return items;
    }

    private static object UpdatePath(NovaraDatabase db, string id, string? name, string? path, string? note)
    {
        McpLogic.UpdatePath(db, id, name, path, note);
        return true;
    }




    private static object Delete(NovaraStore store, string type, string id, ref Action? postGate)
    {
        if (!store.Database.AppSettings.McpDeleteEnabled)
            throw new McpError("MCP 删除操作未开启，请在 Novara 设置中打开权限");
        McpLogic.DeleteItem(store.Database, type, id);



        if ((type == "todo" || type == "note") && Guid.TryParse(id, out var delId))
        {
            var card = type == "todo"
                ? store.Database.TodoCards.FirstOrDefault(x => x.Id == delId) as object
                : store.Database.NoteCards.FirstOrDefault(x => x.Id == delId);




            DateTime? reminderAt = card switch
            {
                Models.TodoCard t => t.ReminderAt,
                Models.NoteCard n => n.ReminderAt,
                _ => null,
            };
            if (reminderAt != null)
            {
                Services.ReminderScheduler.ClearFields(card);
                postGate += () => Services.ReminderScheduler.Cancel(delId);
            }
            postGate += () => Services.StickySync.RemoveNote(id);
        }
        return true;
    }



    private static string? GetStr(JsonElement? p, string key)
        => p is { } e && e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;




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
