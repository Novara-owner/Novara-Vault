using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SyncDiffTests
{

    private static readonly Guid MemoA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MemoB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GroupA = Guid.Parse("33333333-3333-3333-3333-333333333333");



    private static MemoEntry Memo(Guid id, string name, bool deleted = false)
        => new() { Id = id, Name = name, Type = "自定义", IsDeleted = deleted };


    private static NovaraDatabase Db() => new();

    private static SyncPartitionDiff MemoDiff(SyncDiffSummary summary)
        => Assert.Single(summary.Partitions, p => p.Partition == SyncPartition.Memo);



    [Fact]
    public void IdenticalDatabases_ReportNoDifference()
    {
        var local = Db();
        var remote = Db();
        local.MemoEntries.Add(Memo(MemoA, "same"));
        remote.MemoEntries.Add(Memo(MemoA, "same"));

        var summary = SyncDiff.Compare(local, remote);

        Assert.True(summary.IsIdentical);
        Assert.Empty(summary.Dirty);
        Assert.Equal(0, summary.OnlyLocal);
        Assert.Equal(0, summary.OnlyRemote);
        Assert.Equal(0, summary.BothChanged);
        Assert.Equal(1, MemoDiff(summary).Same);
    }

    [Fact]
    public void EveryPartitionIsAlwaysReported_InAFixedOrder()
    {
        var summary = SyncDiff.Compare(Db(), Db());

        Assert.Equal(
            new[] { SyncPartition.Memo, SyncPartition.Diary, SyncPartition.Todo, SyncPartition.Note, SyncPartition.FilePath, SyncPartition.MemoGroup },
            summary.Partitions.Select(p => p.Partition).ToArray());
        Assert.True(summary.IsIdentical);
    }



    [Fact]
    public void ItemOnlyThisDeviceHas_CountsAsOnlyLocal()
    {
        var local = Db();
        var remote = Db();
        local.MemoEntries.Add(Memo(MemoA, "mine"));

        var summary = SyncDiff.Compare(local, remote);

        Assert.Equal(1, summary.OnlyLocal);
        Assert.Equal(0, summary.OnlyRemote);
        Assert.False(summary.IsIdentical);

        Assert.Equal("mine", local.MemoEntries[0].Name);
    }

    [Fact]
    public void ItemOnlyTheServerHas_CountsAsOnlyRemote()
    {
        var local = Db();
        var remote = Db();
        remote.MemoEntries.Add(Memo(MemoA, "theirs"));

        var summary = SyncDiff.Compare(local, remote);

        Assert.Equal(0, summary.OnlyLocal);
        Assert.Equal(1, summary.OnlyRemote);
    }

    [Fact]
    public void SameIdWithDifferentContent_CountsAsBothChanged()
    {
        var local = Db();
        var remote = Db();
        local.MemoEntries.Add(Memo(MemoA, "edited here"));
        remote.MemoEntries.Add(Memo(MemoA, "edited there"));

        var summary = SyncDiff.Compare(local, remote);

        Assert.Equal(1, summary.BothChanged);
        Assert.Equal(0, summary.OnlyLocal);
        Assert.Equal(0, summary.OnlyRemote);
    }



    [Fact]
    public void DeletedOnTheServer_CountsAsOnlyLocal()
    {
        var local = Db();
        var remote = Db();
        local.MemoEntries.Add(Memo(MemoA, "still alive here"));
        remote.MemoEntries.Add(Memo(MemoA, "still alive here", deleted: true));

        var summary = SyncDiff.Compare(local, remote);


        Assert.Equal(1, summary.OnlyLocal);
        Assert.Equal(0, summary.BothChanged);
    }

    [Fact]
    public void DeletedOnThisDevice_CountsAsOnlyRemote()
    {
        var local = Db();
        var remote = Db();
        local.MemoEntries.Add(Memo(MemoA, "gone here", deleted: true));
        remote.MemoEntries.Add(Memo(MemoA, "gone here"));

        var summary = SyncDiff.Compare(local, remote);

        Assert.Equal(1, summary.OnlyRemote);
        Assert.Equal(0, summary.BothChanged);
    }

    [Fact]
    public void TombstonedOnBothSides_IsIgnoredEntirely()
    {
        var local = Db();
        var remote = Db();
        local.MemoEntries.Add(Memo(MemoA, "deleted", deleted: true));
        remote.MemoEntries.Add(Memo(MemoA, "deleted", deleted: true));

        var summary = SyncDiff.Compare(local, remote);


        Assert.True(summary.IsIdentical);
        var memo = MemoDiff(summary);
        Assert.Equal(0, memo.OnlyLocal + memo.OnlyRemote + memo.BothChanged + memo.Same);
    }

    [Fact]
    public void TombstonesAndLiveItems_AreCountedSeparately()
    {
        var local = Db();
        var remote = Db();
        local.MemoEntries.Add(Memo(MemoA, "a"));
        local.MemoEntries.Add(Memo(MemoB, "b"));
        remote.MemoEntries.Add(Memo(MemoA, "a"));
        remote.MemoEntries.Add(Memo(MemoB, "b", deleted: true));

        var summary = SyncDiff.Compare(local, remote);

        var memo = MemoDiff(summary);
        Assert.Equal(1, memo.Same);
        Assert.Equal(1, memo.OnlyLocal);
        Assert.False(memo.IsClean);
    }



    [Fact]
    public void MemoGroupsAreCovered_AndRelyOnAbsenceInsteadOfATombstone()
    {
        var local = Db();
        var remote = Db();
        local.MemoGroups.Add(new MemoGroup { Id = GroupA, Name = "renamed here" });
        remote.MemoGroups.Add(new MemoGroup { Id = GroupA, Name = "named there" });

        var changed = SyncDiff.Compare(local, remote);
        Assert.Equal(1, Assert.Single(changed.Partitions, p => p.Partition == SyncPartition.MemoGroup).BothChanged);


        var removed = SyncDiff.Compare(local, Db());
        Assert.Equal(1, Assert.Single(removed.Partitions, p => p.Partition == SyncPartition.MemoGroup).OnlyLocal);
    }



    [Fact]
    public void RoamingSettingsDifference_IsReportedAsAFlag_NotAsAPartition()
    {
        var local = Db();
        var remote = Db();
        local.AppSettings.AppLanguage = "zh-CN";
        remote.AppSettings.AppLanguage = "en-US";

        var summary = SyncDiff.Compare(local, remote);

        Assert.True(summary.RoamingSettingsDiffer);
        Assert.False(summary.IsIdentical);
        Assert.Empty(summary.Dirty);
    }

    [Fact]
    public void NonRoamingSettingsDifference_IsNotReported()
    {
        var local = Db();
        var remote = Db();
        local.AppSettings.McpToken = "local-secret";
        local.AppSettings.AutoStart = true;
        remote.AppSettings.McpToken = "other-secret";


        Assert.True(SyncDiff.Compare(local, remote).IsIdentical);
    }



    [Fact]
    public void DuplicateIds_DoNotThrow()
    {
        var local = Db();
        var remote = Db();
        local.MemoEntries.Add(Memo(MemoA, "first"));
        local.MemoEntries.Add(Memo(MemoA, "second"));

        var summary = SyncDiff.Compare(local, remote);

        Assert.Equal(1, summary.OnlyLocal);
    }

    [Fact]
    public void ItemsWithoutAnId_AreSkipped()
    {
        var local = Db();
        local.MemoEntries.Add(Memo(Guid.Empty, "corrupt"));
        local.DiaryItems.Add(new DiaryEntry { Id = "", Title = "corrupt" });

        var summary = SyncDiff.Compare(local, Db());

        Assert.True(summary.IsIdentical);
    }

    [Fact]
    public void DiaryIdsAreStrings_AndStillMatchAcrossSides()
    {
        var local = Db();
        var remote = Db();
        local.DiaryItems.Add(new DiaryEntry { Id = "2026-09-12-diary", Title = "edited here" });
        remote.DiaryItems.Add(new DiaryEntry { Id = "2026-09-12-diary", Title = "edited there" });

        var summary = SyncDiff.Compare(local, remote);

        Assert.Equal(1, Assert.Single(summary.Partitions, p => p.Partition == SyncPartition.Diary).BothChanged);
    }

    [Fact]
    public void PinOrderAndStarChanges_CountAsContentChanges()
    {
        var local = Db();
        var remote = Db();
        local.PathBackupItems.Add(new FilePathEntry { Id = MemoA, Name = "p", Path = "/x" });
        remote.PathBackupItems.Add(new FilePathEntry { Id = MemoA, Name = "p", Path = "/x", IsPinned = true });


        var summary = SyncDiff.Compare(local, remote);
        Assert.Equal(1, Assert.Single(summary.Partitions, p => p.Partition == SyncPartition.FilePath).BothChanged);
    }

    [Fact]
    public void Dirty_OnlyListsPartitionsThatNeedADecision()
    {
        var local = Db();
        var remote = Db();
        local.TodoCards.Add(new TodoCard { Id = MemoA, Title = "todo" });
        local.NoteCards.Add(new NoteCard { Id = MemoA, Title = "note" });
        remote.NoteCards.Add(new NoteCard { Id = MemoA, Title = "note" });

        var summary = SyncDiff.Compare(local, remote);

        Assert.Equal(new[] { SyncPartition.Todo }, summary.Dirty.Select(p => p.Partition).ToArray());
        Assert.Equal(1, summary.OnlyLocal);
    }
}
