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

    public string? MemoType { get; set; }
    public string? KeyInfo { get; set; }
    public List<McpFieldView>? Fields { get; set; }

    public string? MainText { get; set; }
    public List<string>? SubTexts { get; set; }
    public List<bool>? CheckedStates { get; set; }

    public string? Content { get; set; }
    public string? Format { get; set; }

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










    private static readonly HashSet<string> SensitiveLabels = BuildSensitiveLabels();

    private static HashSet<string> BuildSensitiveLabels()
    {
        var set = new HashSet<string>(MemoFieldMask.MaskedLabelNames, StringComparer.OrdinalIgnoreCase);

        set.UnionWith(new[]
        {
            "password", "密钥", "secret", "token", "key", "api key", "apikey", "api_key", "passwd",
            "cvv", "totp"
        });
        return set;
    }

    public static bool IsSensitiveLabel(string? label)
        => !string.IsNullOrWhiteSpace(label) && SensitiveLabels.Contains(label.Trim());

    public static bool IsValidType(string? type) => type != null && AllTypes.Contains(type);



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
            MemoType = e.Type,




            KeyInfo = MemoFieldMask.IsMaskedKeyInfo(e.Type) ? "****" : e.KeyInfo,
            Fields = fields
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



    public static List<McpSearchHit> SearchItems(NovaraDatabase db, string query, string? type)
    {
        var result = new List<McpSearchHit>();
        if (string.IsNullOrWhiteSpace(query)) return result;
        query = query.Trim();

        if (type == null || type == TypeMemo)
            foreach (var e in db.MemoEntries)
                if (!e.IsDeleted)
                {


                    string? hayKeyInfo = MemoFieldMask.IsMaskedKeyInfo(e.Type) ? null : e.KeyInfo;
                    var hay = string.Join("\n", new[] { e.Name, hayKeyInfo }
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





    private static readonly HashSet<string> MemoTypes = new(MemoEntryTypes.All, StringComparer.Ordinal);

    public static string CreateMemo(NovaraDatabase db, string name, string type, string? keyInfo,
        List<McpFieldInput>? fields, Guid? groupId, string? iconKey, string? workspaceId = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new McpError("备忘名称 name 必填");

        type = string.IsNullOrWhiteSpace(type) ? "自定义" : type.Trim();
        if (!MemoTypes.Contains(type)) throw new McpError($"未知备忘类型: {type}（可选：邮箱/账户/API Key/网站/银行卡/WiFi/证件/自定义）");
        if (groupId.HasValue && !db.MemoGroups.Any(g => g.Id == groupId.Value))
            throw new McpError("分组不存在");
        var e = new MemoEntry
        {
            Name = name.Trim(), Type = type,
            KeyInfo = keyInfo ?? "", IconKey = iconKey ?? "",
            CreatedAt = DateTime.Now, GroupId = groupId,
            WorkspaceId = workspaceId ?? "",
            Fields = (fields ?? new()).Select(f => new EntryField { Label = f.Label, Value = f.Value, CanCopy = f.CanCopy }).ToList()
        };

        db.MemoEntries.Add(e);
        return e.Id.ToString();
    }

    public static string CreateTodo(NovaraDatabase db, string title, string mainText, List<string>? subTexts, string? iconKey, string? workspaceId = null)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new McpError("待办标题 title 必填");
        if (string.IsNullOrWhiteSpace(mainText)) throw new McpError("待办内容 mainText 必填");
        var e = new TodoCard
        {
            Title = title.Trim(), MainText = mainText.Trim(), IconKey = iconKey ?? "",
            SubTexts = (subTexts ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList(),
            CreatedAt = DateTime.Now, WorkspaceId = workspaceId ?? ""
        };
        e.CheckedStates = Enumerable.Repeat(false, e.SubTexts.Count + 1).ToList();
        if (db.TodoCards.Any(x => x.Order > 0) || db.NoteCards.Any(x => x.Order > 0)) e.Order = 1;
        db.TodoCards.Add(e);
        return e.Id.ToString();
    }

    public static string CreateNote(NovaraDatabase db, string title, string content, string? iconKey, string? workspaceId = null)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new McpError("便签标题 title 必填");
        if (string.IsNullOrWhiteSpace(content)) throw new McpError("便签内容 content 必填");
        var e = new NoteCard { Title = title.Trim(), Content = content, IconKey = iconKey ?? "", CreatedAt = DateTime.Now, WorkspaceId = workspaceId ?? "" };
        if (db.TodoCards.Any(x => x.Order > 0) || db.NoteCards.Any(x => x.Order > 0)) e.Order = 1;
        db.NoteCards.Add(e);
        return e.Id.ToString();
    }

    public static string CreateDiary(NovaraDatabase db, string title, string content, string? format, string? workspaceId = null)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new McpError("文档标题 title 必填");
        if (string.IsNullOrWhiteSpace(content)) throw new McpError("文档内容 content 必填");
        var fmt = string.IsNullOrWhiteSpace(format) ? "markdown" : format.Trim().ToLowerInvariant();
        if (fmt != "markdown" && fmt != "html") throw new McpError("format 仅支持 markdown 或 html");
        var e = new DiaryEntry { Title = TruncateTitle(title.Trim()), Content = content, Format = fmt, CreatedAt = DateTime.Now, ModifiedAt = DateTime.Now, WorkspaceId = workspaceId ?? "" };
        if (db.DiaryItems.Any(x => x.Order > 0)) e.Order = 1;
        db.DiaryItems.Add(e);
        return e.Id;
    }


    private static string TruncateTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return title;
        int nonSpace = 0;
        foreach (var c in title) if (!char.IsWhiteSpace(c)) nonSpace++;
        if (nonSpace <= 120) return title;
        var sb = new System.Text.StringBuilder();
        int kept = 0;
        foreach (var c in title)
        {
            if (!char.IsWhiteSpace(c))
            {
                if (kept >= 120) break;
                kept++;
            }
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    public static string CreatePath(NovaraDatabase db, string name, string path, string? note, string? workspaceId = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new McpError("路径名称 name 必填");
        if (string.IsNullOrWhiteSpace(path)) throw new McpError("路径 path 必填");
        var e = new FilePathEntry { Name = name.Trim(), Path = path.Trim(), Note = note ?? "", CreatedAt = DateTime.Now, WorkspaceId = workspaceId ?? "" };

        db.PathBackupItems.Add(e);
        return e.Id.ToString();
    }



    public static void UpdateMemo(NovaraDatabase db, string id, string? name, string? type, string? keyInfo,
        List<McpFieldInput>? fields, Guid? groupId, string? iconKey, bool clearGroupId = false)
    {
        var e = FindMemo(db, id);






        var newName = name?.Trim();
        if (name != null && string.IsNullOrWhiteSpace(name)) throw new McpError("name 不能为空");
        var newType = type?.Trim();
        if (type != null && !MemoTypes.Contains(newType!))
            throw new McpError($"未知备忘类型: {newType}（可选：邮箱/账户/API Key/网站/银行卡/WiFi/证件/自定义）");






        if (type != null && !string.Equals(e.Type, newType, StringComparison.Ordinal)
            && MemoFieldMask.IsMaskedKeyInfo(e.Type) && !MemoFieldMask.IsMaskedKeyInfo(newType!)
            && !string.IsNullOrEmpty(e.KeyInfo) && keyInfo == null)
        {
            throw new McpError("不能把带卡号/证件号的条目改成非掩码类型（会绕过脱敏）。请先重写或清空 keyInfo。");
        }
        if (!clearGroupId && groupId.HasValue && !db.MemoGroups.Any(g => g.Id == groupId.Value))
            throw new McpError("分组不存在");

        List<EntryField>? newFields = null;
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



            foreach (var g in e.Fields.Where(x => IsSensitiveLabel(x.Label) && !string.IsNullOrEmpty(x.Value)))
            {
                bool movedToNonSensitive = fields.Any(f =>
                    !IsSensitiveLabel(f.Label) && string.Equals(f.Value, g.Value, StringComparison.Ordinal));
                if (movedToNonSensitive)
                    throw new McpError($"不能把敏感字段「{g.Label}」的值移动到非敏感标签下（会绕过脱敏）。请先清除该值再改名。");
            }
            newFields = fields.Select(f => new EntryField { Label = f.Label, Value = f.Value, CanCopy = f.CanCopy }).ToList();
        }


        if (name != null) e.Name = newName!;
        if (type != null) e.Type = newType!;
        if (keyInfo != null) e.KeyInfo = keyInfo;
        if (iconKey != null) e.IconKey = iconKey;



        bool ownershipChanged = clearGroupId || (groupId.HasValue && (!e.GroupId.HasValue || e.GroupId.Value != groupId.Value));
        if (clearGroupId) e.GroupId = null;
        else if (groupId.HasValue) e.GroupId = groupId;
        if (ownershipChanged) { e.IsPinned = false; e.IsStarred = false; }
        if (newFields != null) e.Fields = newFields;
    }

    public static void UpdateTodo(NovaraDatabase db, string id, string? title, string? mainText, List<string>? subTexts, string? iconKey)
    {
        var e = FindTodo(db, id);


        var newTitle = title?.Trim();
        if (title != null && string.IsNullOrWhiteSpace(title)) throw new McpError("title 不能为空");
        var newMainText = mainText?.Trim();
        if (mainText != null && string.IsNullOrWhiteSpace(mainText)) throw new McpError("mainText 不能为空");

        if (title != null) e.Title = newTitle!;
        if (mainText != null) e.MainText = newMainText!;
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

        var newTitle = title?.Trim();
        if (title != null && string.IsNullOrWhiteSpace(title)) throw new McpError("title 不能为空");
        if (content != null && string.IsNullOrWhiteSpace(content)) throw new McpError("content 不能为空");

        if (title != null) e.Title = newTitle!;
        if (content != null) e.Content = content;
        if (iconKey != null) e.IconKey = iconKey;
    }

    public static void UpdateDiary(NovaraDatabase db, string id, string? title, string? content)
    {
        var e = FindDiary(db, id);



        var newTitle = title?.Trim();
        if (title != null && string.IsNullOrWhiteSpace(title)) throw new McpError("title 不能为空");



        if (content != null && string.IsNullOrWhiteSpace(content)) throw new McpError("content 不能为空");



        bool changed = false;
        if (title != null)
        {
            var t = TruncateTitle(newTitle!);
            if (!string.Equals(e.Title, t, StringComparison.Ordinal)) { e.Title = t; changed = true; }
        }
        if (content != null && !string.Equals(e.Content, content, StringComparison.Ordinal)) { e.Content = content; changed = true; }
        if (changed) e.ModifiedAt = DateTime.Now;
    }

    public static void UpdatePath(NovaraDatabase db, string id, string? name, string? path, string? note)
    {
        var e = FindPath(db, id);


        var newName = name?.Trim();
        if (name != null && string.IsNullOrWhiteSpace(name)) throw new McpError("name 不能为空");
        var newPath = path?.Trim();
        if (path != null && string.IsNullOrWhiteSpace(path)) throw new McpError("path 不能为空");

        if (name != null) e.Name = newName!;
        if (path != null) e.Path = newPath!;
        if (note != null) e.Note = note;
    }



    public static void DeleteItem(NovaraDatabase db, string type, string id)
    {
        switch (type)
        {

            case TypeMemo: { var e = FindMemo(db, id); e.IsDeleted = true; e.DeletedAt = DateTime.Now; e.IsPinned = false; e.IsStarred = false; break; }
            case TypeTodo: { var e = FindTodo(db, id); e.IsDeleted = true; e.DeletedAt = DateTime.Now; e.IsPinned = false; e.IsStarred = false; break; }
            case TypeNote: { var e = FindNote(db, id); e.IsDeleted = true; e.DeletedAt = DateTime.Now; e.IsPinned = false; e.IsStarred = false; break; }
            case TypeDiary: { var e = FindDiary(db, id); e.IsDeleted = true; e.DeletedAt = DateTime.Now; e.IsPinned = false; e.IsStarred = false; break; }
            case TypePath: { var e = FindPath(db, id); e.IsDeleted = true; e.DeletedAt = DateTime.Now; e.IsPinned = false; e.IsStarred = false; break; }
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
