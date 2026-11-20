using System.Text;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SealExportTests
{
    private const string SpacePassword = "Space-export-2026-alpha-9";
    private const string BackupPassword = "Backup-export-2026-bravo-4";

    private static readonly SealKdfParams FastKdf = SealKdfParams.Create(8 * 1024, 1);

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string PathOf(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {  }
        }
    }

    private static async Task<SealService> UnlockedServiceAsync(TempDir dir, string name)
    {
        var paths = new SealSpacePaths(dir.PathOf(name));
        paths.EnsureCreated();

        var service = new SealService(paths, new SealSpaceStore(paths, FastKdf));
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(SpacePassword));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(SpacePassword));
        return service;
    }

    private static async Task<SealService> ServiceWithItemsAsync(TempDir dir, int count)
    {
        var service = await UnlockedServiceAsync(dir, "space");

        var source = dir.PathOf("src");
        Directory.CreateDirectory(source);

        var requests = new List<SealRequest>();
        for (var i = 0; i < count; i++)
        {
            var file = Path.Combine(source, $"file{i}.txt");
            await File.WriteAllTextAsync(file, $"payload {i}", new UTF8Encoding(false));
            requests.Add(new SealRequest(file));
        }

        var sealedResult = await service.SealAsync(requests);
        Assert.Equal(SealServiceStatus.Ok, sealedResult.Status);
        foreach (var item in sealedResult.Value!) Assert.True(item.Committed, $"{item.ItemId}: {item.Error}");

        return service;
    }

    [Fact]
    public async Task KindOneExportsTheEntriesAndTheyImportBack()
    {
        using var dir = new TempDir();
        var service = await ServiceWithItemsAsync(dir, 3);

        var bundle = dir.PathOf("Novara_Space_20261106.novabundle");
        Assert.Equal(SealServiceStatus.Ok,
            await service.ExportAsync(bundle, SpacePassword, SealBundleWriter.KindItems));

        var peek = SealBundleReader.Peek(bundle);
        Assert.Equal(SealBundleReadStatus.Ok, peek.Status);
        Assert.Equal(SealBundleWriter.KindItems, peek.Peek!.Kind);
        Assert.False(peek.Peek.HasBackup);
        Assert.Equal(3, peek.Peek.ItemCount);


        var target = new SealSpacePaths(dir.PathOf("target"));
        target.EnsureCreated();
        var imported = await SealImport.RunAsync(bundle, SpacePassword, target);
        Assert.Equal(SealImportStatus.Ok, imported.Status);
        Assert.Equal(3, imported.Counts.Imported);
    }

    [Fact]
    public async Task KindTwoEmbedsABackupThatStillRestores()
    {
        using var dir = new TempDir();
        var service = await ServiceWithItemsAsync(dir, 2);


        var library = new NovaraStore(dir.PathOf("library.novadb"));
        library.Load();
        library.Database.MemoEntries.Add(new MemoEntry
        {
            Name = "导出样例：资料库条目",
            Type = "自定义",
            Fields = [new EntryField { Label = "说明", Value = "资料库恢复后应能看到", CanCopy = true }],
        });
        Assert.True(library.SaveSync());

        var backupFile = dir.PathOf("library.novaenc");
        Assert.True(library.ExportBackupEncrypted(backupFile, BackupPassword, includeFilePathEntries: false));
        var backup = await File.ReadAllBytesAsync(backupFile);

        var bundle = dir.PathOf("Novara_Backup_20261106.novabundle");
        Assert.Equal(SealServiceStatus.Ok,
            await service.ExportAsync(bundle, SpacePassword, SealBundleWriter.KindItemsWithBackup, backup));

        var peek = SealBundleReader.Peek(bundle);
        Assert.True(peek.Peek!.HasBackup);
        Assert.Equal(2, peek.Peek.ItemCount);


        var extracted = dir.PathOf("extracted.novaenc");
        Assert.Equal(SealBundleReadStatus.Ok,
            await SealBundleReader.ExtractBackupAsync(bundle, SpacePassword, extracted));

        var restored = new NovaraStore(dir.PathOf("restored.novadb"));
        Assert.Equal(LoadStatus.Ok, restored.ImportBackup(extracted, BackupPassword).Status);
        Assert.Contains(restored.Database.MemoEntries, m => m.Name == "导出样例：资料库条目");


        var wrong = new NovaraStore(dir.PathOf("wrong.novadb"));
        Assert.NotEqual(LoadStatus.Ok, wrong.ImportBackup(extracted, SpacePassword).Status);
    }

    [Fact]
    public async Task AWrongPasswordWritesNothing()
    {
        using var dir = new TempDir();
        var service = await ServiceWithItemsAsync(dir, 1);

        var bundle = dir.PathOf("never.novabundle");
        Assert.Equal(SealServiceStatus.WrongPassword,
            await service.ExportAsync(bundle, "not-the-password", SealBundleWriter.KindItems));


        Assert.False(File.Exists(bundle));
        Assert.False(File.Exists(bundle + ".tmp"));
    }

    [Fact]
    public async Task ADirectSessionCannotExport()
    {
        using var dir = new TempDir();
        var paths = new SealSpacePaths(dir.PathOf("space"));
        paths.EnsureCreated();

        var service = new SealService(paths, new SealSpaceStore(paths, FastKdf));
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(SpacePassword));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(SpacePassword, SealSessionMode.Direct));

        Assert.Equal(SealServiceStatus.RequiresFullSession,
            await service.ExportAsync(dir.PathOf("x.novabundle"), SpacePassword, SealBundleWriter.KindItems));
        Assert.False(File.Exists(dir.PathOf("x.novabundle")));
    }





    [Fact]
    public async Task AnExportRefusesWhileAnotherWriteOwnsTheSpace()
    {
        using var dir = new TempDir();
        var service = await ServiceWithItemsAsync(dir, 1);


        Assert.True(await service.WriteGate.WaitAsync(TimeSpan.FromSeconds(5)));
        try
        {
            var target = dir.PathOf("busy.novabundle");
            Assert.Equal(SealServiceStatus.Busy,
                await service.ExportAsync(target, SpacePassword, SealBundleWriter.KindItems));
            Assert.False(File.Exists(target));
            Assert.False(File.Exists(target + ".tmp"));
        }
        finally
        {
            service.WriteGate.Release();
        }


        Assert.Equal(SealServiceStatus.Ok,
            await service.ExportAsync(dir.PathOf("ok.novabundle"), SpacePassword, SealBundleWriter.KindItems));
    }





    [Fact]
    public async Task AnExportRefusesWhileTheJournalNeedsRecovery()
    {
        using var dir = new TempDir();
        var service = await ServiceWithItemsAsync(dir, 1);

        File.WriteAllBytes(Path.Combine(dir.PathOf("space"), "journal.nsj"), [0x00, 0x11, 0x22, 0x33]);

        var bundle = dir.PathOf("refused.novabundle");
        Assert.Equal(SealServiceStatus.Corrupt, await service.ExportAsync(bundle, SpacePassword, SealBundleWriter.KindItems));
        Assert.False(File.Exists(bundle));
        Assert.False(File.Exists(bundle + ".tmp"));
    }
}
