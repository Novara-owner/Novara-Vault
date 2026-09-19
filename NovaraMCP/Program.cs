using System.Text;
using System.Text.Json.Nodes;

namespace NovaraMCP;

public static class Program
{
    private const string ProtocolVersion = "2024-11-05";


    private static readonly string[] SupportedVersions = { "2024-11-05", "2025-03-26", "2025-06-18" };

    private static string? _token;
    private static McpPipeClient _pipe = new(null);

    public static int Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        _token = Environment.GetEnvironmentVariable("NOVARA_MCP_TOKEN");
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--token" && i + 1 < args.Length) { _token = args[i + 1]; i++; }
        }
        _pipe = new McpPipeClient(_token);

        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            try { ProcessLine(line); }
            catch (Exception ex) { Console.Error.WriteLine($"NovaraMCP error: {ex.Message}"); }
        }
        return 0;
    }



    private static string? GetStr(JsonNode? node)
        => node is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static void ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        JsonObject? obj;
        try { obj = JsonNode.Parse(line) as JsonObject; }
        catch
        {




            WriteFrame(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = null,
                ["error"] = new JsonObject { ["code"] = -32700, ["message"] = "Parse error" }
            });
            return;
        }
        if (obj == null)
        {






            if (JsonNode.Parse(line) is JsonArray batch)
            {
                var responses = new JsonArray();
                foreach (var el in batch)
                    if (el is JsonObject o && ProcessRequest(o) is JsonNode response) responses.Add(response);
                if (responses.Count > 0) WriteFrame(responses);
            }
            return;
        }

        if (ProcessRequest(obj) is JsonNode frame) WriteFrame(frame);
    }



    private static JsonNode? ProcessRequest(JsonObject obj)
    {
        var id = obj["id"];
        try
        {
            var method = GetStr(obj["method"]) ?? "";

            switch (method)
            {
                case "initialize": return HandleInitialize(id, obj);
                case "tools/list": return HandleToolsList(id);
                case "tools/call": return HandleToolsCall(id, obj);
                case "ping": return Frame(id, new JsonObject());
                default:



                    if (id != null)
                        return Frame(id, null, new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" });
                    return null;
            }
        }
        catch (Exception ex)
        {


            if (id != null)
                return Frame(id, null, new JsonObject { ["code"] = -32600, ["message"] = $"Invalid request: {ex.Message}" });
            return null;
        }
    }


    private static JsonObject? Frame(JsonNode? id, JsonNode? result, JsonObject? error = null)
    {
        if (id == null) return null;
        var resp = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone() };
        if (error != null) resp["error"] = error;
        else resp["result"] = result ?? new JsonObject();
        return resp;
    }

    private static void WriteFrame(JsonNode resp)
    {
        Console.WriteLine(resp.ToJsonString());
        Console.Out.Flush();
    }

    private static JsonObject? HandleInitialize(JsonNode? id, JsonObject obj)
    {

        var requested = GetStr(obj["params"]?["protocolVersion"]);
        var version = requested != null && SupportedVersions.Contains(requested) ? requested : ProtocolVersion;
        return Frame(id, new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },


            ["serverInfo"] = new JsonObject { ["name"] = "novara-mcp", ["version"] = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0" }
        });
    }

    private static JsonObject? HandleToolsList(JsonNode? id)
    {
        var tools = new JsonArray();
        foreach (var t in ToolSchemas()) tools.Add(t);
        return Frame(id, new JsonObject { ["tools"] = tools });
    }

    private static JsonObject? HandleToolsCall(JsonNode? id, JsonObject obj)
    {
        var name = GetStr(obj["params"]?["name"]) ?? "";
        var args = obj["params"]?["arguments"] as JsonObject;

        try
        {
            var resultJson = _pipe.Call(name, args);
            var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = resultJson } };
            return Frame(id, new JsonObject { ["content"] = content });
        }
        catch (McpForwardError ex)
        {
            var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = ex.Message } };
            return Frame(id, new JsonObject { ["content"] = content, ["isError"] = true });
        }
        catch (Exception ex)
        {

            System.Diagnostics.Debug.WriteLine($"NovaraMCP 内部错误: {ex}");
            return Frame(id, null, new JsonObject { ["code"] = -32603, ["message"] = "内部错误" });
        }
    }



    private static JsonObject Str(string desc) => new() { ["type"] = "string", ["description"] = desc };
    private static JsonObject Bool(string desc) => new() { ["type"] = "boolean", ["description"] = desc };
    private static JsonObject Enum(string desc, params string[] vals) =>
        new() { ["type"] = "string", ["description"] = desc, ["enum"] = new JsonArray(vals.Select(v => (JsonNode)v!).ToArray()) };
    private static JsonObject StrArr(string desc) =>
        new() { ["type"] = "array", ["description"] = desc, ["items"] = new JsonObject { ["type"] = "string" } };
    private static JsonObject FieldsSchema() => new()
    {
        ["type"] = "array",
        ["description"] = "字段列表（备忘条目的自定义字段）",
        ["items"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["label"] = Str("字段名（如「密码」「API Key」）"),
                ["value"] = Str("字段值"),
                ["canCopy"] = Bool("是否可一键复制")
            }
        }
    };

    private static JsonObject Tool(string name, string desc, JsonObject props, params string[] required) => new()
    {
        ["name"] = name,
        ["description"] = desc,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(required.Select(r => (JsonNode)r!).ToArray())
        }
    };

    private static JsonObject TypeEnum(string desc) => Enum(desc, "memo", "todo", "note", "diary", "path");
    private static JsonObject MemoTypeEnum(string desc) => Enum(desc, "邮箱", "账户", "API Key", "网站", "银行卡", "WiFi", "证件", "自定义");

    private static List<JsonObject> ToolSchemas() => new()
    {
        Tool("create_memo", "新建备忘条目（邮箱/账户/API Key/网站/银行卡/WiFi/证件/自定义）",
            new JsonObject
            {
                ["name"] = Str("条目名称"),
                ["type"] = MemoTypeEnum("条目类型"),
                ["keyInfo"] = Str("关键信息（邮箱地址/账号/URL）"),
                ["fields"] = FieldsSchema(),
                ["groupId"] = Str("所属分组 ID（可空=未分类）"),
                ["iconKey"] = Str("图标键")
            }, "name", "type"),
        Tool("create_todo", "新建待办卡片",
            new JsonObject
            {
                ["title"] = Str("卡片标题"),
                ["mainText"] = Str("主待办内容"),
                ["subTexts"] = StrArr("子待办列表"),
                ["iconKey"] = Str("图标键")
            }, "title", "mainText"),
        Tool("create_note", "新建便签卡片",
            new JsonObject
            {
                ["title"] = Str("便签标题"),
                ["content"] = Str("便签内容"),
                ["iconKey"] = Str("图标键")
            }, "title", "content"),
        Tool("create_diary", "新建日记/文档（HTML 会经白名单净化）",
            new JsonObject
            {
                ["title"] = Str("标题"),
                ["content"] = Str("内容"),
                ["format"] = Enum("格式", "markdown", "html")
            }, "title", "content"),
        Tool("create_path", "新建路径备份条目",
            new JsonObject
            {
                ["name"] = Str("名称"),
                ["path"] = Str("文件/文件夹路径"),
                ["note"] = Str("备注")
            }, "name", "path"),

        Tool("update_memo", "修改备忘（不能篡改已有敏感字段，可新增敏感字段）",
            new JsonObject
            {
                ["id"] = Str("条目 ID"),
                ["name"] = Str("条目名称"),
                ["type"] = MemoTypeEnum("条目类型"),
                ["keyInfo"] = Str("关键信息"),
                ["fields"] = FieldsSchema(),
                ["groupId"] = Str("所属分组 ID"),
                ["iconKey"] = Str("图标键")
            }, "id"),
        Tool("update_todo", "修改待办（注意：传 subTexts 会按新列表整体重建，已有勾选状态会被重置为未勾选）",
            new JsonObject
            {
                ["id"] = Str("待办 ID"),
                ["title"] = Str("标题"),
                ["mainText"] = Str("主待办内容"),
                ["subTexts"] = StrArr("子待办列表"),
                ["iconKey"] = Str("图标键")
            }, "id"),
        Tool("update_note", "修改便签",
            new JsonObject
            {
                ["id"] = Str("便签 ID"),
                ["title"] = Str("标题"),
                ["content"] = Str("内容"),
                ["iconKey"] = Str("图标键")
            }, "id"),
        Tool("update_diary", "修改日记/文档（format 不可改）",
            new JsonObject
            {
                ["id"] = Str("文档 ID"),
                ["title"] = Str("标题"),
                ["content"] = Str("内容")
            }, "id"),
        Tool("update_path", "修改路径备份",
            new JsonObject
            {
                ["id"] = Str("路径 ID"),
                ["name"] = Str("名称"),
                ["path"] = Str("路径"),
                ["note"] = Str("备注")
            }, "id"),

        Tool("delete_item", "软删除条目（进回收站；需在 Novara 设置中开启删除权限）",
            new JsonObject
            {
                ["type"] = TypeEnum("条目类型"),
                ["id"] = Str("条目 ID")
            }, "type", "id"),

        Tool("list_items", "列出条目摘要（不含正文、不含回收站）",
            new JsonObject
            {
                ["type"] = TypeEnum("条目类型（可选，不传=全部）")
            }),
        Tool("read_item", "读取单条全文（敏感字段已脱敏）",
            new JsonObject
            {
                ["type"] = TypeEnum("条目类型"),
                ["id"] = Str("条目 ID")
            }, "type", "id"),
        Tool("search_items", "全类型全文搜索（排除回收站）",
            new JsonObject
            {
                ["query"] = Str("搜索关键词"),
                ["type"] = TypeEnum("条目类型（可选）")
            }, "query"),
    };
}
