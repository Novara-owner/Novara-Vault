using System.Security.Cryptography;
using System.Text;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealSealOperationTests
{
    private static readonly byte[] Vmk =
        Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x60 + i)).ToArray();

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-op-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) {  }
        }
    }

    private static SealSpacePaths NewPaths(TempDir dir)
    {
        var paths = new SealSpacePaths(Path.Combine(dir.Root, "seal"));
        paths.EnsureCreated();


        SealIndex.Empty.Save(paths.Index, Vmk);
        return paths;
    }

    private static string WriteFile(TempDir dir, string name, byte[] content)
    {
        var path = Path.Combine(dir.Root, name);
        File.WriteAllBytes(path, content);
        return path;
    }


    private static async Task<byte[]> ReadContainerAsync(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(SealContainerHeader.TryRead(bytes, out var header, out _));
        Assert.True(SealKeySlot.TryUnwrap(
            Vmk, header.KeySlot, bytes.AsSpan(0, SealFormat.AuthenticatedPrefixSize), out var contentKey, out _));

        var plain = new MemoryStream();
        await SealContainerReader.ReadAsync(
            new MemoryStream(bytes, writable: false),
            contentKey,
            (chunk, _) =>
            {
                plain.Write(chunk.Span);
                return Task.CompletedTask;
            });

        return plain.ToArray();
    }

    private sealed class RecordingPorts : ISealSealPorts
    {
        public List<string> Calls { get; } = [];

        public SealIndex Index { get; private set; } = SealIndex.Empty;

        public SealJournal Journal { get; private set; } = SealJournal.Empty;

        public SealIndex? SavedIndex { get; private set; }

        public void AppendJournal(SealJournalRecord record)
        {
            Journal = Journal.Append(record);
            Calls.Add("journal:" + record.Phase);
        }

        public void RemoveJournal(string opId)
        {
            Journal = Journal.Remove(opId);
            Calls.Add("journal:remove");
        }

        public void SaveIndex(SealIndex index)
        {
            Index = index;
            SavedIndex = index;
            Calls.Add("index:" + index.Items.Count);
        }

        public void MarkVerified(string itemId) => Calls.Add("verify");

        public bool DeleteSource(string path, bool isDirectory)
        {
            Calls.Add("delete");
            return true;
        }
    }


    private sealed class RefusingDeletePorts(SealSpacePaths paths, byte[] vmk) : SealSealPorts(paths, vmk)
    {
        public override bool DeleteSource(string path, bool isDirectory) => false;
    }

    private sealed class CollectingProgress : IProgress<SealProgress>
    {
        public List<SealProgress> Reports { get; } = [];

        public void Report(SealProgress value) => Reports.Add(value);
    }

    [Fact]
    public async Task ANormalSealMovesTheFileAndLeavesAVerifiableContainer()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var content = Encoding.UTF8.GetBytes("这是要被封存的内容");
        var source = WriteFile(dir, "旅行照片.jpg", content);

        var operation = new SealSealOperation(paths, Vmk);
        var result = Assert.Single(await operation.RunAsync([new SealRequest(source)]));

        Assert.True(result.SourceDeleted);
        Assert.True(result.IndexUpdated);
        Assert.True(result.Committed);
        Assert.Equal(SealSealError.None, result.Error);
        Assert.Equal(32, result.ItemId.Length);

        Assert.False(File.Exists(source));

        var index = SealIndex.Load(paths.Index, Vmk);
        var item = Assert.Single(index.Items);
        Assert.Equal(result.ItemId, item.Id);
        Assert.Equal("旅行照片.jpg", item.OriginalName);
        Assert.Equal((ulong)content.Length, item.Size);
        Assert.Equal(DigestHex(content), item.ContentSha256);
        Assert.Null(item.FolderEntryCount);


        Assert.Equal(item.SealedAtUtc, index.ObservedMaxUtc);

        var container = paths.ItemPath(result.ItemId);
        Assert.True(File.Exists(container));
        Assert.Equal(content, await ReadContainerAsync(container));
        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
    }

    [Fact]
    public async Task ASourceThatCannotBeDeleted_IsReportedAndStaysAnOrphan()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = WriteFile(dir, "locked.bin", [1, 2, 3, 4]);

        var ports = new RefusingDeletePorts(paths, Vmk);
        var operation = new SealSealOperation(paths, Vmk, ports);
        var result = Assert.Single(await operation.RunAsync([new SealRequest(source)]));


        Assert.False(result.SourceDeleted);
        Assert.True(result.IndexUpdated);
        Assert.Equal(SealSealError.SourceDeleteFailed, result.Error);
        Assert.True(File.Exists(source));
        Assert.Single(SealIndex.Load(paths.Index, Vmk).Items);

        var journal = SealJournal.Load(paths.Journal, Vmk);
        Assert.Equal(3, journal.Records.Count);
        Assert.Equal(SealJournalPhase.SealIndexed, journal.Records[^1].Phase);
        Assert.DoesNotContain(journal.Records, r => r.Phase == SealJournalPhase.Deleted);


        var index = SealIndex.Load(paths.Index, Vmk);
        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));
        Assert.Equal(SealRecoveryActionKind.OrphanSourceRemains, Assert.Single(plan.Actions).Kind);
    }

    [Fact]
    public async Task TheSequenceIsJournalContainerVerifyIndexDelete()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = WriteFile(dir, "order.bin", [9, 8, 7]);

        var ports = new RecordingPorts();
        var operation = new SealSealOperation(paths, Vmk, ports);
        var result = Assert.Single(await operation.RunAsync([new SealRequest(source)]));
        Assert.True(result.SourceDeleted);

        Assert.Equal(
            [
                "journal:seal_begin",
                "verify",
                "journal:sealed",
                "index:1",
                "journal:seal_indexed",
                "delete",
                "journal:deleted",
                "journal:remove",
            ],
            ports.Calls);



        var verify = ports.Calls.IndexOf("verify");
        var sealedRecord = ports.Calls.IndexOf("journal:sealed");
        var indexed = ports.Calls.IndexOf("index:1");
        var sealIndexed = ports.Calls.IndexOf("journal:seal_indexed");
        var delete = ports.Calls.IndexOf("delete");
        var deleted = ports.Calls.IndexOf("journal:deleted");

        Assert.True(verify < sealedRecord);
        Assert.True(indexed < delete);
        Assert.True(sealIndexed < delete);
        Assert.True(deleted > delete);
        Assert.Single(ports.SavedIndex!.Items);
    }

    [Fact]
    public async Task CancellingKeepsTheOriginalAndLeavesNoFragment()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);


        var content = new byte[SealFormat.DefaultChunkSize + 1024];
        Random.Shared.NextBytes(content);
        var source = WriteFile(dir, "big.bin", content);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var operation = new SealSealOperation(paths, Vmk);
        var result = Assert.Single(await operation.RunAsync([new SealRequest(source)], null, cts.Token));

        Assert.Equal(SealSealError.Cancelled, result.Error);
        Assert.False(result.SourceDeleted);
        Assert.False(result.IndexUpdated);
        Assert.True(File.Exists(source));
        Assert.Equal(content, File.ReadAllBytes(source));

        Assert.Empty(SealIndex.Load(paths.Index, Vmk).Items);
        Assert.Empty(Directory.EnumerateFiles(paths.Items));
        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
    }

    [Fact]
    public async Task AnEmptyFileSealsToOneHeaderBlock()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = WriteFile(dir, "empty.txt", []);

        var operation = new SealSealOperation(paths, Vmk);
        var result = Assert.Single(await operation.RunAsync([new SealRequest(source)]));

        Assert.True(result.Committed);
        Assert.Equal(SealFormat.HeaderSize, new FileInfo(paths.ItemPath(result.ItemId)).Length);
        Assert.Empty(await ReadContainerAsync(paths.ItemPath(result.ItemId)));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task ThreeItemsStayIndependent()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var good = WriteFile(dir, "good.bin", [1, 2, 3]);
        var busy = WriteFile(dir, "busy.bin", [4, 5, 6]);
        var inside = Path.Combine(paths.Items, "already-inside.bin");
        File.WriteAllBytes(inside, [7, 8, 9]);

        var progress = new CollectingProgress();
        using var holder = new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.True(holder.CanRead);

        var operation = new SealSealOperation(paths, Vmk);
        var results = await operation.RunAsync(
            [new SealRequest(good), new SealRequest(busy), new SealRequest(inside)], progress);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Committed);

        Assert.Equal(SealSealError.Rejected, results[1].Error);
        Assert.Equal(SealRejectReason.InUse, results[1].Reject);

        Assert.Equal(SealSealError.Rejected, results[2].Error);
        Assert.Equal(SealRejectReason.InsideSealRoot, results[2].Reject);


        Assert.Single(SealIndex.Load(paths.Index, Vmk).Items);
        Assert.False(File.Exists(good));
        Assert.True(File.Exists(busy));
        Assert.True(File.Exists(inside));

        Assert.Equal(3, progress.Reports.Count);
        Assert.Equal(new SealProgress(1, 3, "good.bin"), progress.Reports[0]);
        Assert.Equal(new SealProgress(3, 3, "already-inside.bin"), progress.Reports[2]);
    }

    [Fact]
    public async Task AFolderKeepsItsEntryCountAndTotalSize()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var folder = Path.Combine(dir.Root, "项目资料");
        Directory.CreateDirectory(Path.Combine(folder, "子目录"));
        Directory.CreateDirectory(Path.Combine(folder, "空目录"));
        File.WriteAllBytes(Path.Combine(folder, "a.txt"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(folder, "子目录", "b.txt"), [4, 5, 6, 7]);

        var operation = new SealSealOperation(paths, Vmk);
        var result = Assert.Single(await operation.RunAsync([new SealRequest(folder)]));

        Assert.True(result.Committed);
        Assert.False(Directory.Exists(folder));

        var item = Assert.Single(SealIndex.Load(paths.Index, Vmk).Items);
        Assert.Equal(4, item.FolderEntryCount);
        Assert.Equal((ulong)7, item.Size);

        var container = paths.ItemPath(result.ItemId);
        var containerBytes = File.ReadAllBytes(container);
        Assert.True(SealContainerHeader.TryRead(containerBytes, out var header, out _));



        Assert.True(header.PlaintextLength > 7);
        Assert.Equal(SealFormat.FlagFolderArchive, header.Flags);

        var plaintext = await ReadContainerAsync(container);
        Assert.Equal((ulong)plaintext.Length, header.PlaintextLength);
        Assert.Equal((byte)'P', plaintext[0]);
        Assert.Equal((byte)'K', plaintext[1]);

        Assert.Empty(Directory.EnumerateFiles(paths.Staging, "pack-*.zip"));
    }





    [Fact]
    public async Task AFailedFolderSealKeepsItsPackAndJournalForRecovery()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var folder = Path.Combine(dir.Root, "待封存资料");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "a.txt"), [1, 2, 3]);


        Directory.Delete(paths.Items, recursive: true);

        var operation = new SealSealOperation(paths, Vmk);
        var result = Assert.Single(await operation.RunAsync([new SealRequest(folder)]));

        Assert.False(result.Committed);
        Assert.Equal(SealSealError.WriteFailed, result.Error);
        Assert.True(Directory.Exists(folder));

        var pack = Assert.Single(Directory.EnumerateFiles(paths.Staging, "pack-*.zip"));
        var journal = SealJournal.Load(paths.Journal, Vmk);
        Assert.Contains(journal.Records,
            r => r.ItemId == result.ItemId && r.Phase == SealJournalPhase.SealBegin);

        var index = SealIndex.Load(paths.Index, Vmk);
        SealRecovery.Sweep(paths, index, journal);
        Assert.True(File.Exists(pack));

        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));
        var discard = plan.Actions.First(a =>
            a.Kind is SealRecoveryActionKind.OfferRetrySeal or SealRecoveryActionKind.StagingOnly);
        Assert.True(await SealRecovery.ResolveAsync(
            index, journal, paths, Vmk, discard, SealRecoveryChoice.DiscardStaging,
            new SealFileProbe(paths, index)));
        Assert.False(File.Exists(pack));
    }

    [Fact]
    public async Task AnInvalidRequestIsRefusedWithoutTouchingAnything()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = WriteFile(dir, "raw.bin", [1]);

        var operation = new SealSealOperation(paths, Vmk);


        var noExpiry = Assert.Single(await operation.RunAsync(
            [new SealRequest(source, Kind: SealedItemKind.Capsule)]));
        Assert.Equal(SealSealError.InvalidRequest, noExpiry.Error);
        Assert.True(File.Exists(source));



        var zeroExpiry = Assert.Single(await operation.RunAsync(
            [new SealRequest(source, Kind: SealedItemKind.Capsule, ExpirySeconds: 0)]));
        Assert.Equal(SealSealError.InvalidRequest, zeroExpiry.Error);


        var hugeExpiry = Assert.Single(await operation.RunAsync(
            [new SealRequest(source, Kind: SealedItemKind.Capsule, ExpirySeconds: long.MaxValue)]));
        Assert.Equal(SealSealError.InvalidRequest, hugeExpiry.Error);

        Assert.Empty(SealIndex.Load(paths.Index, Vmk).Items);
        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
    }

    [Fact]
    public async Task EachCapsuleCountsItsWindowFromItsOwnSealMoment()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var first = WriteFile(dir, "first.bin", [1]);
        var second = WriteFile(dir, "second.bin", [2]);

        var operation = new SealSealOperation(paths, Vmk);
        var results = await operation.RunAsync(
        [
            new SealRequest(first, Kind: SealedItemKind.Capsule, ExpirySeconds: 60),
            new SealRequest(second, Kind: SealedItemKind.Capsule, ExpirySeconds: 60),
        ]);
        Assert.All(results, r => Assert.Equal(SealSealError.None, r.Error));



        var items = SealIndex.Load(paths.Index, Vmk).Items;
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(60, item.ExpiryUtc - item.SealedAtUtc));
    }

    [Fact]
    public void ClearReadOnlyAttributes_MakesADeletePossible()
    {
        using var dir = new TempDir();
        var folder = Path.Combine(dir.Root, "只读文件夹");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "file.txt");
        File.WriteAllBytes(file, [1, 2, 3]);

        File.SetAttributes(file, FileAttributes.ReadOnly);
        File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);

        SealSealPorts.ClearReadOnlyAttributes(file);
        Assert.Equal(0, (int)(File.GetAttributes(file) & FileAttributes.ReadOnly));

        var ports = new SealSealPorts(NewPaths(dir), Vmk);
        Assert.True(ports.DeleteSource(folder, isDirectory: true));
        Assert.False(Directory.Exists(folder));
    }






    [Fact]
    public async Task AnItemIdCollision_KeepsTheExistingContainer()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var first = Encoding.UTF8.GetBytes("第一份内容");
        var firstSource = WriteFile(dir, "first.txt", first);

        var operation = new SealSealOperation(paths, Vmk);
        var committed = Assert.Single(await operation.RunAsync([new SealRequest(firstSource)]));
        Assert.True(committed.Committed);

        var second = Encoding.UTF8.GetBytes("第二份内容");
        var secondSource = WriteFile(dir, "second.txt", second);
        var retry = new SealSealOperation(paths, Vmk, newId: () => committed.ItemId);
        var result = Assert.Single(await retry.RunAsync([new SealRequest(secondSource)]));

        Assert.Equal(SealSealError.ItemIdCollision, result.Error);
        Assert.False(result.Committed);


        Assert.True(File.Exists(paths.ItemPath(committed.ItemId)));
        Assert.Equal(first, await ReadContainerAsync(paths.ItemPath(committed.ItemId)));
        var index = SealIndex.Load(paths.Index, Vmk);
        Assert.Single(index.Items);
        Assert.Equal(committed.ItemId, Assert.Single(index.Items).Id);


        Assert.True(File.Exists(secondSource));
    }

    private static string DigestHex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
