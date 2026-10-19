using System.Text.Json;
using Novara.Models;

namespace Novara.Services;


public enum SyncPartition
{
    Memo,
    Diary,
    Todo,
    Note,
    FilePath,
    MemoGroup,
}





public sealed record SyncPartitionDiff(
    SyncPartition Partition,
    int OnlyLocal,
    int OnlyRemote,
    int BothChanged,
    int Same)
{

    public bool IsClean => OnlyLocal == 0 && OnlyRemote == 0 && BothChanged == 0;
}






public sealed record SyncDiffSummary(IReadOnlyList<SyncPartitionDiff> Partitions, bool RoamingSettingsDiffer)
{
    public int OnlyLocal => Partitions.Sum(p => p.OnlyLocal);
    public int OnlyRemote => Partitions.Sum(p => p.OnlyRemote);
    public int BothChanged => Partitions.Sum(p => p.BothChanged);


    public bool IsIdentical => OnlyLocal == 0 && OnlyRemote == 0 && BothChanged == 0 && !RoamingSettingsDiffer;


    public IReadOnlyList<SyncPartitionDiff> Dirty => Partitions.Where(p => !p.IsClean).ToList();
}










public static class SyncDiff
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static SyncDiffSummary Compare(NovaraDatabase local, NovaraDatabase remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        var partitions = new List<SyncPartitionDiff>
        {
            Compare(local.MemoEntries, remote.MemoEntries, SyncPartition.Memo),
            Compare(local.DiaryItems, remote.DiaryItems, SyncPartition.Diary),
            Compare(local.TodoCards, remote.TodoCards, SyncPartition.Todo),
            Compare(local.NoteCards, remote.NoteCards, SyncPartition.Note),
            Compare(local.PathBackupItems, remote.PathBackupItems, SyncPartition.FilePath),

            Compare(local.MemoGroups, remote.MemoGroups, SyncPartition.MemoGroup, hasTombstone: false),



        };

        return new SyncDiffSummary(partitions, RoamingSettingsDiffer(local.AppSettings, remote.AppSettings));
    }


    private static bool RoamingSettingsDiffer(AppSettings? local, AppSettings? remote)
        => !string.Equals(local?.AppLanguage, remote?.AppLanguage, StringComparison.Ordinal)
           || !string.Equals(local?.Theme, remote?.Theme, StringComparison.Ordinal)
           || WorkspacesDiffer(local, remote);






    private static bool WorkspacesDiffer(AppSettings? local, AppSettings? remote)
    {
        var rw = remote?.Workspaces;
        if (rw is not { Count: > 0 }) return false;
        var lw = local?.Workspaces;
        if (lw is null || lw.Count != rw.Count) return true;
        for (int i = 0; i < rw.Count; i++)
        {
            var a = lw[i]; var b = rw[i];
            if (!string.Equals(a?.Id, b?.Id, StringComparison.Ordinal)
                || !string.Equals(a?.Name, b?.Name, StringComparison.Ordinal)
                || !string.Equals(a?.IconKey, b?.IconKey, StringComparison.Ordinal)
                || a?.CreatedAt != b?.CreatedAt)
                return true;
        }
        return false;
    }

    private static SyncPartitionDiff Compare<T>(
        List<T> local, List<T> remote, SyncPartition partition, bool hasTombstone = true)
        where T : class
    {
        var localMap = Index(local);
        var remoteMap = Index(remote);

        int onlyLocal = 0, onlyRemote = 0, bothChanged = 0, same = 0;

        foreach (var id in localMap.Keys.Union(remoteMap.Keys))
        {
            var hasLocal = localMap.TryGetValue(id, out var localItem);
            var hasRemote = remoteMap.TryGetValue(id, out var remoteItem);

            var localLive = hasLocal && !IsDeleted(localItem!, hasTombstone);
            var remoteLive = hasRemote && !IsDeleted(remoteItem!, hasTombstone);

            if (!localLive && !remoteLive) continue;
            if (localLive && !remoteLive) { onlyLocal++; continue; }
            if (!localLive && remoteLive) { onlyRemote++; continue; }

            if (string.Equals(ContentKey(localItem!), ContentKey(remoteItem!), StringComparison.Ordinal)) same++;
            else bothChanged++;
        }

        return new SyncPartitionDiff(partition, onlyLocal, onlyRemote, bothChanged, same);
    }







    private static Dictionary<string, T> Index<T>(List<T> items) where T : class
    {
        var map = new Dictionary<string, T>();
        foreach (var item in items)
        {
            var key = KeyOf(item);
            if (key.Length > 0) map[key] = item;
        }
        return map;
    }






    private static string KeyOf(object item) => item switch
    {
        MemoEntry e => Normalize(e.Id),
        DiaryEntry e => e.Id ?? "",
        TodoCard e => Normalize(e.Id),
        NoteCard e => Normalize(e.Id),
        FilePathEntry e => Normalize(e.Id),
        MemoGroup e => Normalize(e.Id),
        _ => "",
    };

    private static string Normalize(Guid id) => id == Guid.Empty ? "" : id.ToString();

    private static bool IsDeleted(object item, bool hasTombstone)
        => hasTombstone && item switch
        {
            MemoEntry e => e.IsDeleted,
            DiaryEntry e => e.IsDeleted,
            TodoCard e => e.IsDeleted,
            NoteCard e => e.IsDeleted,
            FilePathEntry e => e.IsDeleted,
            _ => false,
        };





    private static string ContentKey(object item) => JsonSerializer.Serialize(item, item.GetType(), Options);
}
