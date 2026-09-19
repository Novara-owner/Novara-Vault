using Novara.Models;

namespace Novara.Services;

[Flags]
public enum McpPerm : long
{
    None = 0,
    MemoRead = 1L << 0, MemoCreate = 1L << 1, MemoUpdate = 1L << 2, MemoDelete = 1L << 3,
    PathRead = 1L << 4, PathCreate = 1L << 5, PathUpdate = 1L << 6, PathDelete = 1L << 7,
    TodoRead = 1L << 8, TodoCreate = 1L << 9, TodoUpdate = 1L << 10, TodoDelete = 1L << 11,
    NoteRead = 1L << 12, NoteCreate = 1L << 13, NoteUpdate = 1L << 14, NoteDelete = 1L << 15,
    DiaryRead = 1L << 16, DiaryCreate = 1L << 17, DiaryUpdate = 1L << 18, DiaryDelete = 1L << 19,
}

public static class McpPermissions
{


    public const McpPerm AllRead =
        McpPerm.MemoRead | McpPerm.PathRead | McpPerm.TodoRead | McpPerm.NoteRead | McpPerm.DiaryRead;




    public const McpPerm DefaultForNewClient =
        McpPerm.PathRead | McpPerm.TodoRead | McpPerm.NoteRead | McpPerm.DiaryRead;



    public const McpPerm LegacyFull =
        McpPerm.MemoRead | McpPerm.MemoCreate | McpPerm.MemoUpdate | McpPerm.MemoDelete |
        McpPerm.PathRead | McpPerm.PathCreate | McpPerm.PathUpdate | McpPerm.PathDelete |
        McpPerm.TodoRead | McpPerm.TodoCreate | McpPerm.TodoUpdate | McpPerm.TodoDelete |
        McpPerm.NoteRead | McpPerm.NoteCreate | McpPerm.NoteUpdate | McpPerm.NoteDelete |
        McpPerm.DiaryRead | McpPerm.DiaryCreate | McpPerm.DiaryUpdate | McpPerm.DiaryDelete;


    public const McpPerm AllDelete =
        McpPerm.MemoDelete | McpPerm.PathDelete | McpPerm.TodoDelete | McpPerm.NoteDelete | McpPerm.DiaryDelete;











    public static McpPerm ApplyDeleteGate(McpPerm fromUi, McpPerm onRecord, bool deleteGateOff)
        => deleteGateOff ? (fromUi & ~AllDelete) | (onRecord & AllDelete) : fromUi;

    private static (McpPerm r, McpPerm c, McpPerm u, McpPerm d)? Zone(string? type) => type switch
    {
        "memo" => (McpPerm.MemoRead, McpPerm.MemoCreate, McpPerm.MemoUpdate, McpPerm.MemoDelete),
        "path" => (McpPerm.PathRead, McpPerm.PathCreate, McpPerm.PathUpdate, McpPerm.PathDelete),
        "todo" => (McpPerm.TodoRead, McpPerm.TodoCreate, McpPerm.TodoUpdate, McpPerm.TodoDelete),
        "note" => (McpPerm.NoteRead, McpPerm.NoteCreate, McpPerm.NoteUpdate, McpPerm.NoteDelete),
        "diary" => (McpPerm.DiaryRead, McpPerm.DiaryCreate, McpPerm.DiaryUpdate, McpPerm.DiaryDelete),
        _ => null,
    };


    public static McpPerm RequiredFor(string method, string? typeArg)
    {
        if (method == "delete_item")
        {
            var z = Zone(typeArg);
            return z?.d ?? McpPerm.None;
        }
        if (method.StartsWith("create_") || method.StartsWith("update_"))
        {
            var z = Zone(method[(method.IndexOf('_') + 1)..]);
            if (z == null) return McpPerm.None;
            return method[0] == 'c' ? z.Value.c : z.Value.u;
        }
        if (method is "list_items" or "read_item" or "search_items")
        {




            if (string.IsNullOrEmpty(typeArg)) return AllRead;
            var z = Zone(typeArg);
            return z?.r ?? McpPerm.None;
        }
        return McpPerm.None;
    }


    public static string Describe(McpPerm p)
    {
        if (p == McpPerm.None) return "none";
        if ((p & AllRead) == AllRead && (p & ~AllRead) == 0) return "all:read";
        var parts = new List<string>();
        foreach (var (bit, name) in Bits())
            if (p.HasFlag(bit)) parts.Add(name);
        return parts.Count > 0 ? string.Join(" + ", parts) : "none";
    }

    private static IEnumerable<(McpPerm bit, string name)> Bits()
    {
        string[] zones = { "memo", "path", "todo", "note", "diary" };
        string[] ops = { "read", "create", "update", "delete" };
        long baseBit = 1;
        for (int z = 0; z < 5; z++)
            for (int o = 0; o < 4; o++)
                yield return ((McpPerm)(baseBit << (z * 4 + o)), zones[z] + ":" + ops[o]);
    }

    public static McpPerm GetFor(AppSettings s, string clientPath)
    {





        try
        {
            var rec = s.McpClientPermissions.ToList()
                .FirstOrDefault(r => string.Equals(r.Path, clientPath, StringComparison.OrdinalIgnoreCase));
            return rec?.Permissions != null ? (McpPerm)rec.Permissions : McpPerm.None;
        }
        catch { return McpPerm.None; }
    }






    public static void EnsureDefaultRecord(AppSettings s, string clientPath)
    {
        if (s.McpClientPermissions.Any(r => string.Equals(r.Path, clientPath, StringComparison.OrdinalIgnoreCase))) return;
        s.McpClientPermissions.Add(new McpClientPermRecord { Path = clientPath, Permissions = (long)DefaultForNewClient });
    }





    public static bool EnsureMigrated(AppSettings s)
    {
        if (s.McpPermMigrated) return false;
        s.McpPermMigrated = true;
        foreach (var p in s.McpAllowedProcesses)
            if (!s.McpClientPermissions.Any(r => string.Equals(r.Path, p, StringComparison.OrdinalIgnoreCase)))
                s.McpClientPermissions.Add(new McpClientPermRecord { Path = p, Permissions = (long)LegacyFull });
        return true;
    }
}
