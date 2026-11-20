using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SealIntegrationTests
{
    private static readonly SealKdfParams FastKdf = SealKdfParams.Create(8 * 1024, 1);
    private const string SpacePassword = "Space-9-tunnel-42";
    private const string BackupPassword = "Backup-9-tunnel-42";

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-integration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string PathOf(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {  }
        }
    }

    private static SealSpacePaths NewSpace(TempDir dir)
    {
        var paths = new SealSpacePaths(dir.PathOf("seal"));
        paths.EnsureCreated();
        return paths;
    }

    private static NovaraStore NewStore(TempDir dir, string name = "data.novadb")
    {
        var store = new NovaraStore(dir.PathOf(name));
        store.Load();
        store.Database.MemoEntries.Add(new MemoEntry { Name = "集成条目", Type = "自定义" });
        return store;
    }

    private static void AssertBlind(object value, string what)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Assert.DoesNotContain("seal", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("novabundle", json, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("privatespace", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrEmpty(what));
    }

    [Fact]
    public void TheFourteenMcpToolsNeverSeeThePrivateSpace()
    {
        var db = new NovaraDatabase();
        var memoType = McpLogic.AllTypes[0];

        var memo = McpLogic.CreateMemo(db, "备忘一", "自定义", "值", null, null, null);
        var todo = McpLogic.CreateTodo(db, "待办一", "正文", null, null);
        var note = McpLogic.CreateNote(db, "便签一", "内容", null);
        var diary = McpLogic.CreateDiary(db, "文档一", "正文", "markdown");
        var path = McpLogic.CreatePath(db, "路径一", @"D:\docs", null);

        AssertBlind(McpLogic.ListItems(db, null, McpPermissions.AllRead), "list_items");
        AssertBlind(McpLogic.ReadItem(db, memoType, memo), "read_item");
        AssertBlind(McpLogic.SearchItems(db, "备忘", null, McpPermissions.AllRead), "search_items");

        McpLogic.UpdateMemo(db, memo, "备忘二", "自定义", null, null, null, null);
        McpLogic.UpdateTodo(db, todo, "待办二", "正文二", null, null);
        McpLogic.UpdateNote(db, note, "便签二", "内容二", null);
        McpLogic.UpdateDiary(db, diary, "文档二", "正文二");
        McpLogic.UpdatePath(db, path, "路径二", @"D:\docs2", null);

        AssertBlind(McpLogic.ListItems(db, null, McpPermissions.AllRead), "list_items after update");
        AssertBlind(db, "the whole database");

        McpLogic.DeleteItem(db, memoType, memo);
        AssertBlind(db, "the whole database after delete");
    }

    [Fact]
    public void TheSnapshotTemplateCarriesNoSealMagic()
    {
        var root = RepositoryRoot();
        var template = File.ReadAllText(Path.Combine(root, "SnapshotViewer", "index.html"));
        var script = File.ReadAllText(Path.Combine(root, "SnapshotViewer", "viewer.js"));

        foreach (var text in new[] { template, script })
        {
            Assert.DoesNotContain("NOVASEAL", text, StringComparison.Ordinal);
            Assert.DoesNotContain("NSEALIDX", text, StringComparison.Ordinal);
            Assert.DoesNotContain("novabundle", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheSyncPayloadCarriesNoSealMagic()
    {
        var db = new NovaraDatabase();
        db.MemoEntries.Add(new MemoEntry { Name = "同步条目", Type = "自定义" });

        var clone = SyncFieldPolicy.CloneForSync(db);
        var json = JsonSerializer.SerializeToUtf8Bytes(clone, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        var text = Encoding.UTF8.GetString(json);
        Assert.DoesNotContain("NOVASEAL", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NSEALIDX", text, StringComparison.Ordinal);
        Assert.DoesNotContain("seal", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privatespace", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheAuditSanitiserReplacesTheSpacePath()
    {
        var message = @"读取 C:\Users\x\AppData\Local\Novara\seal\items\a.novaseal 失败";
        var cleaned = AuditSanitizer.Clean(message);

        Assert.DoesNotContain(@"\seal\", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("a.novaseal", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<seal>", cleaned, StringComparison.Ordinal);


        var posix = AuditSanitizer.Clean("open /home/x/.local/share/Novara/seal/index.nseal failed");
        Assert.DoesNotContain("/seal/", posix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<seal>", posix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABackupWithoutThePrivateSpaceIsByteIdenticalToTheLegacyExport()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);

        var first = dir.PathOf("plain-1.novabak");
        var second = dir.PathOf("plain-2.novabak");

        Assert.True(SealIntegration.ExportBackupWithoutSeal(store, first, includeFilePathEntries: true));



        var paths = NewSpace(dir);
        var space = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, space.Initialize(SpacePassword, null, out var vmk));

        try
        {
            await SeedOneItem(paths, vmk);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }

        Assert.True(SealIntegration.ExportBackupWithoutSeal(store, second, includeFilePathEntries: true));

        var one = File.ReadAllBytes(first);
        var two = File.ReadAllBytes(second);

        Assert.Equal(one, two);
        Assert.DoesNotContain("NOVASEAL", Encoding.UTF8.GetString(one), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABackupWithThePrivateSpaceIsAKindTwoBundleWithTwoIndependentPasswords()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);
        var paths = NewSpace(dir);

        var space = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, space.Initialize(SpacePassword, null, out var vmk));

        string id;
        try
        {
            id = await SeedOneItem(paths, vmk);

            var bundle = dir.PathOf("full.novabundle");
            var status = await SealIntegration.ExportBackupWithSealAsync(
                store, bundle, includeFilePathEntries: true, BackupPassword, SpacePassword, paths, vmk);

            Assert.Equal(SealBackupStatus.Ok, status);
            Assert.False(File.Exists(bundle + ".backup.tmp.novaenc"));

            var file = File.ReadAllBytes(bundle);
            Assert.Equal("NVSEALBK", Encoding.ASCII.GetString(file, 0, 8));
            Assert.Equal(SealBundleWriter.KindItemsWithBackup, file[9]);


            var indexLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(32, 4));
            var offset = SealBundleWriter.HeaderSize + SealKeyWrap.FileSize + indexLength;
            var blockLength = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(offset, 8));
            var block = dir.PathOf("embedded.novaenc");
            File.WriteAllBytes(block, file.AsSpan(offset + 8, blockLength).ToArray());

            Assert.Equal(LoadStatus.Ok, new NovaraStore(dir.PathOf("restored.novadb")).ImportBackup(block, BackupPassword).Status);
            Assert.NotEqual(LoadStatus.Ok, new NovaraStore(dir.PathOf("restored2.novadb")).ImportBackup(block, SpacePassword).Status);


            var opened = await SealBundleReader.OpenAsync(bundle, SpacePassword);
            Assert.Equal(SealBundleReadStatus.Ok, opened.Status);
            Assert.Single(opened.Bundle!.Items);
            Assert.Equal(id, opened.Bundle.Items[0].ItemId);

            var byBackupPassword = await SealBundleReader.OpenAsync(bundle, BackupPassword);
            Assert.Equal(SealBundleReadStatus.WrongPassword, byBackupPassword.Status);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task ResetDatabaseLeavesThePrivateSpaceUntouched()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);
        var paths = NewSpace(dir);

        var space = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, space.Initialize(SpacePassword, null, out var vmk));

        try
        {
            var id = await SeedOneItem(paths, vmk);
            var container = paths.ItemPath(id);
            var before = SHA256.HashData(File.ReadAllBytes(container));
            var indexBefore = SHA256.HashData(File.ReadAllBytes(paths.Index));

            store.ResetDatabase();

            Assert.True(File.Exists(container));
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(container)));
            Assert.Equal(indexBefore, SHA256.HashData(File.ReadAllBytes(paths.Index)));
            Assert.True(File.Exists(paths.KeyWrap));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task ClearingThePrivateSpaceDeletesNothingElse()
    {
        using var dir = new TempDir();
        var paths = NewSpace(dir);

        var neighbour = dir.PathOf("keep-me");
        Directory.CreateDirectory(neighbour);
        File.WriteAllText(Path.Combine(neighbour, "note.txt"), "keep");

        var space = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, space.Initialize(SpacePassword, null, out var vmk));

        try
        {
            await SeedOneItem(paths, vmk);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }

        Assert.Equal(SealStoreOutcome.Ok, SealIntegration.ClearPrivateSpace(paths));

        Assert.False(Directory.Exists(paths.Root));
        Assert.True(File.Exists(Path.Combine(neighbour, "note.txt")));
    }

    private static async Task<string> SeedOneItem(SealSpacePaths paths, byte[] vmk)
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var plaintext = Encoding.UTF8.GetBytes("integration payload");
        var contentKey = Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x55 + i)).ToArray();

        var header = SealContainerWriter.PrepareHeader(
            Convert.FromHexString(id), (ulong)plaintext.Length, 1_790_000_000, SealFormat.MinChunkSize, vmk, contentKey);

        await using (var destination = new FileStream(paths.ItemPath(id), FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await SealContainerWriter.WriteAsync(
                new MemoryStream(plaintext), header, contentKey, destination, null, CancellationToken.None);
        }

        CryptographicOperations.ZeroMemory(contentKey);

        SealIndex.Empty.WithItem(new SealedItem
        {
            Id = id,
            DisplayName = "集成条目.bin",
            OriginalName = "集成条目.bin",
            SourcePath = @"D:\src\integration.bin",
            Size = (ulong)plaintext.Length,
            SealedAtUtc = 1_790_000_000,
            Kind = SealedItemKind.Normal,
            ContentSha256 = Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant(),
        }).Save(paths.Index, vmk);

        return id;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Novara.csproj")))
            directory = directory.Parent;

        if (directory is null) throw new InvalidOperationException("repository root not found");
        return directory.FullName;
    }
}
