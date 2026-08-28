
using Novara.Models;

namespace Novara.Services;


public class McpError : Exception
{
    public McpError(string message) : base(message) { }
}




public class McpItemSummary
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }   
    public bool IsPinned { get; set; }
    public bool IsStarred { get; set; }
    public string? GroupId { get; set; }        
}


public class McpItemView
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public bool IsPinned { get; set; }
    public bool IsStarred { get; set; }
    public string? GroupId { get; set; }
    public string? GroupName { get; set; }
    // memo
    public string? MemoType { get; set; }
    public string? KeyInfo { get; set; }
    public List<McpFieldView>? Fields { get; set; }
    // todo
    public string? MainText { get; set; }
    public List<string>? SubTexts { get; set; }
    public List<bool>? CheckedStates { get; set; }
    // note / diary
    public string? Content { get; set; }
    public string? Format { get; set; }        
    // path
    public string? Path { get; set; }
    public string? Note { get; set; }
}


public class McpFieldView
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public bool CanCopy { get; set; }
    public bool Redacted { get; set; }
}


public class McpFieldInput
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public bool CanCopy { get; set; }
}


public class McpSearchHit
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Snippet { get; set; } = "";
}

public static class McpLogic
{
    public const string TypeMemo = "memo";
    public const string TypeTodo = "todo";
    public const string TypeNote = "note";
    public const string TypeDiary = "diary";
    public const string TypePath = "path";

    public static readonly string[] AllTypes = { TypeMemo, TypeTodo, TypeNote, TypeDiary, TypePath };

    
    private static readonly HashSet<string> SensitiveLabels = new(StringComparer.Ordinal)
    {
        "密码", "password", "密钥", "secret", "token", "key", "api key", "apikey", "api_key", "passwd"
    };

    public static bool IsSensitiveLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;
        return SensitiveLabels.Contains(label.Trim().ToLowerInvariant());
    }

    public static bool IsValidType(string? type) => type != null && AllTypes.Contains(type);

    // ===================== list =====================

    public static List<McpItemSummary> ListItems(NovaraDatabase db, string? type)
    {
        var result = new List<McpItemSummary>();
        if (type == null || type == TypeMemo)
            foreach (var e in db.MemoEntries)
                if (!e.IsDeleted) result.Add(new McpItemSummary
                {
                    Type = TypeMemo, Id = e.Id.ToString(), Title = e.Name, CreatedAt = e.CreatedAt,
                    IsPinned = e.IsPinned, IsStarred = e.IsStarred, GroupId = e.GroupId?.ToString()
                });
        if (type == null || type == TypeTodo)
            foreach (var e in db.TodoCards)
                if (!e.IsDeleted) result.Add(new McpItemSummary
                {
                    Type = TypeTodo, Id = e.Id.ToString(), Title = e.Title, CreatedAt = e.CreatedAt,
                    IsPinned = e.IsPinned, IsStarred = e.IsStarred
                });
        if (type == null || type == TypeNote)
            foreach (var e in db.NoteCards)
                if (!e.IsDeleted) result.Add(new McpItemSummary
                {
                    Type = TypeNote, Id = e.Id.ToString(), Title = e.Title, CreatedAt = e.CreatedAt,
                    IsPinned = e.IsPinned, IsStarred = e.IsStarred
                });
        if (type == null || type == TypeDiary)
            foreach (var e in db.DiaryItems)
                if (!e.IsDeleted) result.Add(new McpItemSummary
                {
                    Type = TypeDiary, Id = e.Id, Title = e.Title, CreatedAt = e.CreatedAt, ModifiedAt = e.ModifiedAt,
                    IsPinned = e.IsPinned, IsStarred = e.IsStarred
                });
        if (type == null || type == TypePath)
            foreach (var e in db.PathBackupItems)
                if (!e.IsDeleted) result.Add(new McpItemSummary
                {
                    Type = TypePath, Id = e.Id.ToString(), Title = e.Name, CreatedAt = e.CreatedAt,
                    IsPinned = e.IsPinned, IsStarred = e.IsStarred
                });
        return result;
    }

    // ===================== read =====================

    public static McpItemView ReadItem(NovaraDatabase db, string type, string id)
    {
        switch (type)
        {
            case TypeMemo: return ReadMemo(db, id);
            case TypeTodo: return ReadTodo(db, id);
            case TypeNote: return ReadNote(db, id);
            case TypeDiary: return ReadDiary(db, id);
            case TypePath: return ReadPath(db, id);
            default: throw new McpError($"未知类型: {type}");
        }
    }

    private static McpItemView ReadMemo(NovaraDatabase db, string id)
    {
        var e = FindMemo(db, id);
        var fields = e.Fields.Select(f =>
        {
            bool redacted = IsSensitiveLabel(f.Label);
            return new McpFieldView { Label = f.Label, Value = redacted ? "****" : f.Value, CanCopy = f.CanCopy, Redacted = redacted };
        }).ToList();
        return new McpItemView
        {
            Type = TypeMemo, Id = e.Id.ToString(), Title = e.Name, CreatedAt = e.CreatedAt,
            IsPinned = e.IsPinned, IsStarred = e.IsStarred, GroupId = e.GroupId?.ToString(),
            GroupName = e.GroupId is Guid g ? db.MemoGroups.FirstOrDefault(x => x.Id == g)?.Name : null,
            MemoType = e.Type, KeyInfo = e.KeyInfo, Fields = fields
        };
    }

    private static McpItemView ReadTodo(NovaraDatabase db, string id)
    {
        var e = FindTodo(db, id);
        return new McpItemView
        {
            Type = TypeTodo, Id = e.Id.ToString(), Title = e.Title, CreatedAt = e.CreatedAt,
            IsPinned = e.IsPinned, IsStarred = e.IsStarred,
            MainText = e.MainText, SubTexts = e.SubTexts, CheckedStates = e.CheckedStates
        };
    }

    private static McpItemView ReadNote(NovaraDatabase db, string id)
    {
        var e = FindNote(db, id);
        return new McpItemView
        {
            Type = TypeNote, Id = e.Id.ToString(), Title = e.Title, CreatedAt = e.CreatedAt,
            IsPinned = e.IsPinned, IsStarred = e.IsStarred, Content = e.Content
        };
    }

    private static McpItemView ReadDiary(NovaraDatabase db, string id)
    {
        var e = FindDiary(db, id);
        return new McpItemView
        {
            Type = TypeDiary, Id = e.Id, Title = e.Title, CreatedAt = e.CreatedAt, ModifiedAt = e.ModifiedAt,
            IsPinned = e.IsPinned, IsStarred = e.IsStarred, Content = e.Content, Format = e.Format
        };
    }

    private static McpItemView ReadPath(NovaraDatabase db, string id)
    {
        var e = FindPath(db, id);
        return new McpItemView
        {
            Type = TypePath, Id = e.Id.ToString(), Title = e.Name, CreatedAt = e.CreatedAt,
            IsPinned = e.IsPinned, IsStarred = e.IsStarred, Path = e.Path, Note = e.Note
        };
    }

    // ===================== search =====================

    public static List<McpSearchHit> SearchItems(NovaraDatabase db, string query, string? type)
    {
        var result = new List<McpSearchHit>();
        if (string.IsNullOrWhiteSpace(query)) return result;
        query = query.Trim();

        if (type == null || type == TypeMemo)
            foreach (var e in db.MemoEntries)
                if (!e.IsDeleted)
                {
                    var hay = string.Join("\n", new[] { e.Name, e.KeyInfo }
                        .Concat(e.Fields.Where(f => !IsSensitiveLabel(f.Label)).Select(f => $"{f.Label}: {f.Value}")));
                    var snip = SnippetOf(hay, query);
                    if (snip != null) result.Add(new McpSearchHit { Type = TypeMemo, Id = e.Id.ToString(), Title = e.Name, Snippet = snip });
                }
        if (type == null || type == TypeTodo)
            foreach (var e in db.TodoCards)
                if (!e.IsDeleted)
                {
                    var hay = string.Join("\n", new[] { e.Title, e.MainText }.Concat(e.SubTexts));
                    var snip = SnippetOf(hay, query);
                    if (snip != null) result.Add(new McpSearchHit { Type = TypeTodo, Id = e.Id.ToString(), Title = e.Title, Snippet = snip });
                }
        if (type == null || type == TypeNote)
            foreach (var e in db.NoteCards)
                if (!e.IsDeleted)
                {
                    var snip = SnippetOf(e.Title + "\n" + e.Content, query);
                    if (snip != null) result.Add(new McpSearchHit { Type = TypeNote, Id = e.Id.ToString(), Title = e.Title, Snippet = snip });
                }
        if (type == null || type == TypeDiary)
            foreach (var e in db.DiaryItems)
                if (!e.IsDeleted)
                {
                    var snip = SnippetOf(e.Title + "\n" + e.Content, query);
                    if (snip != null) result.Add(new McpSearchHit { Type = TypeDiary, Id = e.Id, Title = e.Title, Snippet = snip });
                }
        if (type == null || type == TypePath)
            foreach (var e in db.PathBackupItems)
                if (!e.IsDeleted)
                {
                    var snip = SnippetOf(e.Name + "\n" + e.Path + "\n" + e.Note, query);
                    if (snip != null) result.Add(new McpSearchHit { Type = TypePath, Id = e.Id.ToString(), Title = e.Name, Snippet = snip });
                }
        return result;
    }

    private static string? SnippetOf(string? haystack, string query)
    {
        if (string.IsNullOrEmpty(haystack)) return null;
        int idx = haystack.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        int start = Math.Max(0, idx - 30);
        int len = Math.Min(80, haystack.Length - start);
        var s = haystack.Substring(start, len).Replace('\n', ' ');
        return (start > 0 ? "…" : "") + s + (start + len < haystack.Length ? "…" : "");
    }

    // ===================== create =====================

    private static readonly HashSet<string> MemoTypes = new()
    { "邮箱", "账户", "API Key", "网站", "银行卡", "WiFi", "证件", "自定义" };

    public static string CreateMemo(NovaraDatabase db, string name, string type, string? keyInfo,
        List<McpFieldInput>? fields, Guid? groupId, string? iconKey)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new McpError("备忘名称 name 必填");
        // NM17: whitelist the type - junk strings used to enter the DB verbatim.
        type = string.IsNullOrWhiteSpace(type) ? "自定义" : type.Trim();
        if (!MemoTypes.Contains(type)) throw new McpError($"未知备忘类型: {type}（可选：邮箱/账户/API Key/网站/银行卡/WiFi/证件/自定义）");
        if (groupId.HasValue && !db.MemoGroups.Any(g => g.Id == groupId.Value))
            throw new McpError("分组不存在");
        var e = new MemoEntry
        {
            Name = name.Trim(), Type = type,
            KeyInfo = keyInfo ?? "", IconKey = iconKey ?? "",
            CreatedAt = DateTime.Now, GroupId = groupId,
            Fields = (fields ?? new()).Select(f => new EntryField { Label = f.Label, Value = f.Value, CanCopy = f.CanCopy }).ToList()
        };
        db.MemoEntries.Add(e);
        return e.Id.ToString();
    }

    public static string CreateTodo(NovaraDatabase db, string title, string mainText, List<string>? subTexts, string? iconKey)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new McpError("待办标题 title 必填");
        if (string.IsNullOrWhiteSpace(mainText)) throw new McpError("待办内容 mainText 必填");
        var e = new TodoCard
        {
            Title = title.Trim(), MainText = mainText.Trim(), IconKey = iconKey ?? "",
            SubTexts = (subTexts ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList(),
            CreatedAt = DateTime.Now
        };
        e.CheckedStates = Enumerable.Repeat(false, e.SubTexts.Count + 1).ToList();
        db.TodoCards.Add(e);
        return e.Id.ToString();
    }

    public static string CreateNote(NovaraDatabase db, string title, string content, string? iconKey)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new McpError("便签标题 title 必填");
        if (string.IsNullOrWhiteSpace(content)) throw new McpError("便签内容 content 必填");
        var e = new NoteCard { Title = title.Trim(), Content = content, IconKey = iconKey ?? "", CreatedAt = DateTime.Now };
        db.NoteCards.Add(e);
        return e.Id.ToString();
    }

    public static string CreateDiary(NovaraDatabase db, string title, string content, string? format)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new McpError("文档标题 title 必填");
        if (content == null) throw new McpError("文档内容 content 必填");
        var fmt = string.IsNullOrWhiteSpace(format) ? "markdown" : format.Trim().ToLowerInvariant();
        if (fmt != "markdown" && fmt != "html") throw new McpError("format 仅支持 markdown 或 html");
        var e = new DiaryEntry { Title = title.Trim(), Content = content, Format = fmt, CreatedAt = DateTime.Now, ModifiedAt = DateTime.Now };
        db.DiaryItems.Add(e);
        return e.Id;
    }

    public static string CreatePath(NovaraDatabase db, string name, string path, string? note)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new McpError("路径名称 name 必填");
        if (string.IsNullOrWhiteSpace(path)) throw new McpError("路径 path 必填");
        var e = new FilePathEntry { Name = name.Trim(), Path = path.Trim(), Note = note ?? "", CreatedAt = DateTime.Now };
        db.PathBackupItems.Add(e);
        return e.Id.ToString();
    }

    // ===================== update =====================

    public static void UpdateMemo(NovaraDatabase db, string id, string? name, string? type, string? keyInfo,
        List<McpFieldInput>? fields, Guid? groupId, string? iconKey)
    {
        var e = FindMemo(db, id);
        if (name != null) { if (string.IsNullOrWhiteSpace(name)) throw new McpError("name 不能为空"); e.Name = name.Trim(); }
        if (type != null) { type = type.Trim(); if (!MemoTypes.Contains(type)) throw new McpError($"未知备忘类型: {type}（可选：邮箱/账户/API Key/网站/银行卡/WiFi/证件/自定义）"); e.Type = type; }
        if (keyInfo != null) e.KeyInfo = keyInfo;
        if (iconKey != null) e.IconKey = iconKey;
        if (groupId.HasValue)
        {
            if (!db.MemoGroups.Any(g => g.Id == groupId.Value)) throw new McpError("分组不存在");
            e.GroupId = groupId;
        }
        if (fields != null)
        {
            foreach (var f in fields)
            {
                if (IsSensitiveLabel(f.Label))
                {
                    var old = e.Fields.FirstOrDefault(x => string.Equals(x.Label, f.Label, StringComparison.OrdinalIgnoreCase));
                    if (old != null && old.Value != f.Value)
                        throw new McpError($"不能修改已有敏感字段「{f.Label}」");
                }
            }
            
            // masking - the same-label check never fired and read/search stopped redacting it.
            // Block any old sensitive VALUE that survives into a now-non-sensitive label.
            foreach (var g in e.Fields.Where(x => IsSensitiveLabel(x.Label) && !string.IsNullOrEmpty(x.Value)))
            {
                bool movedToNonSensitive = fields.Any(f =>
                    !IsSensitiveLabel(f.Label) && string.Equals(f.Value, g.Value, StringComparison.Ordinal));
                if (movedToNonSensitive)
                    throw new McpError($"不能把敏感字段「{g.Label}」的值移动到非敏感标签下（会绕过脱敏）。请先清除该值再改名。");
            }
            e.Fields = fields.Select(f => new EntryField { Label = f.Label, Value = f.Value, CanCopy = f.CanCopy }).ToList();
        }
    }

    public static void UpdateTodo(NovaraDatabase db, string id, string? title, string? mainText, List<string>? subTexts, string? iconKey)
    {
        var e = FindTodo(db, id);
        if (title != null) { if (string.IsNullOrWhiteSpace(title)) throw new McpError("title 不能为空"); e.Title = title.Trim(); }
        if (mainText != null) { if (string.IsNullOrWhiteSpace(mainText)) throw new McpError("mainText 不能为空"); e.MainText = mainText.Trim(); }
        if (iconKey != null) e.IconKey = iconKey;
        if (subTexts != null)
        {
            e.SubTexts = subTexts.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            e.CheckedStates = Enumerable.Repeat(false, e.SubTexts.Count + 1).ToList();
        }
    }

    public static void UpdateNote(NovaraDatabase db, string id, string? title, string? content, string? iconKey)
    {
        var e = FindNote(db, id);
        if (title != null) { if (string.IsNullOrWhiteSpace(title)) throw new McpError("title 不能为空"); e.Title = title.Trim(); }
        if (content != null) { if (string.IsNullOrWhiteSpace(content)) throw new McpError("content 不能为空"); e.Content = content; }
        if (iconKey != null) e.IconKey = iconKey;
    }

    public static void UpdateDiary(NovaraDatabase db, string id, string? title, string? content)
    {
        var e = FindDiary(db, id);
        if (title != null) { if (string.IsNullOrWhiteSpace(title)) throw new McpError("title 不能为空"); e.Title = title.Trim(); }
        if (content != null) e.Content = content;
        e.ModifiedAt = DateTime.Now;
    }

    public static void UpdatePath(NovaraDatabase db, string id, string? name, string? path, string? note)
    {
        var e = FindPath(db, id);
        if (name != null) { if (string.IsNullOrWhiteSpace(name)) throw new McpError("name 不能为空"); e.Name = name.Trim(); }
        if (path != null) { if (string.IsNullOrWhiteSpace(path)) throw new McpError("path 不能为空"); e.Path = path.Trim(); }
        if (note != null) e.Note = note;
    }

    

    public static void DeleteItem(NovaraDatabase db, string type, string id)
    {
        switch (type)
        {
            case TypeMemo: { var e = FindMemo(db, id); e.IsDeleted = true; e.DeletedAt = DateTime.Now; break; }
            case TypeTodo: { var e = FindTodo(db, id); e.IsDeleted = true; e.DeletedAt = DateTime.Now; break; }
            case TypeNote: { var e = FindNote(db, id); e.IsDeleted = true; e.DeletedAt = DateTime.Now; break; }
            case TypeDiary: { var e = FindDiary(db, id); e.IsDeleted = true; e.DeletedAt = DateTime.Now; break; }
            case TypePath: { var e = FindPath(db, id); e.IsDeleted = true; e.DeletedAt = DateTime.Now; break; }
            default: throw new McpError($"未知类型: {type}");
        }
    }

    

    private static MemoEntry FindMemo(NovaraDatabase db, string id)
        => Guid.TryParse(id, out var g) ? db.MemoEntries.FirstOrDefault(x => x.Id == g && !x.IsDeleted)
            ?? throw new McpError("备忘条目不存在") : throw new McpError("非法 id");

    private static TodoCard FindTodo(NovaraDatabase db, string id)
        => Guid.TryParse(id, out var g) ? db.TodoCards.FirstOrDefault(x => x.Id == g && !x.IsDeleted)
            ?? throw new McpError("待办不存在") : throw new McpError("非法 id");

    private static NoteCard FindNote(NovaraDatabase db, string id)
        => Guid.TryParse(id, out var g) ? db.NoteCards.FirstOrDefault(x => x.Id == g && !x.IsDeleted)
            ?? throw new McpError("便签不存在") : throw new McpError("非法 id");

    private static DiaryEntry FindDiary(NovaraDatabase db, string id)
        => db.DiaryItems.FirstOrDefault(x => x.Id == id && !x.IsDeleted)
            ?? throw new McpError("文档不存在");

    private static FilePathEntry FindPath(NovaraDatabase db, string id)
        => Guid.TryParse(id, out var g) ? db.PathBackupItems.FirstOrDefault(x => x.Id == g && !x.IsDeleted)
            ?? throw new McpError("路径条目不存在") : throw new McpError("非法 id");
}
