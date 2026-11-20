using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SealServiceTests
{
    private static readonly SealKdfParams FastKdf = SealKdfParams.Create(8 * 1024, 1);
    private const long Now = 1_790_000_000;
    private const string Password = "Space-9-tunnel-42";

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-service-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string PathOf(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {  }
        }
    }

    private static SealSpacePaths NewPaths(TempDir dir)
    {
        var paths = new SealSpacePaths(dir.PathOf("seal"));
        paths.EnsureCreated();
        return paths;
    }

    private static SealService NewService(SealSpacePaths paths, Func<DateTimeOffset>? clock = null)
        => new(paths, new SealSpaceStore(paths, FastKdf), clock ?? (() => DateTimeOffset.FromUnixTimeSeconds(Now)));

    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static (string Id, byte[] Plain) WriteEntry(SealSpacePaths paths, byte[] vmk, string name, int size)
    {
        var id = NewId();
        var plaintext = new byte[size];
        new Random(size).NextBytes(plaintext);

        var contentKey = Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x40 + i)).ToArray();
        var header = SealContainerWriter.PrepareHeader(
            Convert.FromHexString(id), (ulong)plaintext.Length, Now, SealFormat.MinChunkSize, vmk, contentKey);

        using (var destination = new FileStream(paths.ItemPath(id), FileMode.Create, FileAccess.Write, FileShare.None))
        {
            SealContainerWriter.WriteAsync(
                    new MemoryStream(plaintext), header, contentKey, destination, null, CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        CryptographicOperations.ZeroMemory(contentKey);
        return (id, plaintext);
    }

    private static SealIndex Seed(SealSpacePaths paths, byte[] vmk, params SealedItem[] items)
    {
        var index = SealIndex.Empty;
        foreach (var item in items) index = index.WithItem(item);
        index.Save(paths.Index, vmk);
        return index;
    }

    private static SealedItem Item(string id, byte[] plaintext, string name, bool capsule = false, long? expiry = null)
        => new()
        {
            Id = id,
            DisplayName = name,
            OriginalName = name,
            SourcePath = @"D:\src\" + name,
            Size = (ulong)plaintext.Length,
            SealedAtUtc = Now,
            Kind = capsule ? SealedItemKind.Capsule : SealedItemKind.Normal,
            ExpiryUtc = expiry,
            ContentSha256 = Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant(),
        };

    [Fact]
    public async Task EveryOperationRefusesWithoutASessionAndTouchesNothing()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);

        var id = NewId();
        var target = dir.PathOf("out");
        Directory.CreateDirectory(target);

        Assert.Equal(SealServiceStatus.NotUnlocked, service.Status());

        Assert.Equal(SealServiceStatus.NotUnlocked, service.ListItems().Status);
        Assert.Equal(SealServiceStatus.NotUnlocked, service.CanTake(id).Status);
        Assert.Equal(SealServiceStatus.NotUnlocked,
            (await service.SealAsync([new SealRequest(dir.PathOf("x.bin"))])).Status);
        Assert.Equal(SealServiceStatus.NotUnlocked,
            (await service.TakeAsync(new SealTakeRequest(id, target))).Status);
        Assert.Equal(SealServiceStatus.NotUnlocked, await service.DestroyAsync(id));
        Assert.Equal(SealServiceStatus.NotUnlocked, await service.ChangePasswordAsync("a", "b"));
        Assert.Equal(SealServiceStatus.NotUnlocked, await service.WriteHintAsync("behind"));
        Assert.Equal(SealServiceStatus.NotUnlocked, service.GetUsage().Status);
        Assert.Equal(SealServiceStatus.NotUnlocked, service.GetRecoveryPlan().Status);
        Assert.Equal(SealServiceStatus.NotUnlocked, service.GetRecoveryEntries().Status);
        Assert.Equal(SealServiceStatus.NotUnlocked, (await service.RunAutomaticRecoveryAsync()).Status);
        Assert.Equal(SealServiceStatus.NotUnlocked,
            await service.ApplyRecoveryAsync(
                new SealRecoveryAction(SealRecoveryActionKind.OrphanSourceRemains, "op", id),
                SealRecoveryChoice.TryDeleteSource));
        Assert.Equal(SealServiceStatus.NotUnlocked, await service.ExportAsync(dir.PathOf("x.novabundle"), Password));
        Assert.Equal(SealServiceStatus.NotUnlocked, (await service.ImportAsync(dir.PathOf("x.novabundle"), Password)).Status);
        Assert.Equal(SealServiceStatus.NotUnlocked, await service.ClearAsync());


        Assert.False(File.Exists(paths.KeyWrap));
        Assert.False(File.Exists(paths.Index));
        Assert.False(File.Exists(paths.Journal));
        Assert.Empty(Directory.EnumerateFiles(paths.Root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task TheRollbackGateLocksCapsulesOnly()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var store = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(Password, out var vmk, out _));

        try
        {
            var ordinary = WriteEntry(paths, vmk, "ordinary.bin", 64);
            var capsule = WriteEntry(paths, vmk, "capsule.bin", 64);


            SealIndex.Empty
                .WithItem(Item(ordinary.Id, ordinary.Plain, "ordinary.bin"))
                .WithItem(Item(capsule.Id, capsule.Plain, "capsule.bin", capsule: true, expiry: Now + 30 * 86_400))
                .WithObservedMax(Now + 25 * 3600)
                .Save(paths.Index, vmk);

            Assert.True(service.CanTake(ordinary.Id).Value);
            Assert.False(service.CanTake(capsule.Id).Value);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task ResetClockBaseline_ClearsAFalseRollbackVerdict()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var store = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(Password, out var vmk, out _));
        try
        {
            var capsule = WriteEntry(paths, vmk, "expired-capsule.bin", 64);
            var item = Item(capsule.Id, capsule.Plain, "expired-capsule.bin", capsule: true, expiry: Now - 86_400);
            item.SealedAtUtc = Now - 2 * 86_400;
            SealIndex.Empty
                .WithItem(item)
                .WithObservedMax(Now + 25 * 3600)
                .Save(paths.Index, vmk);

            Assert.False(service.CanTake(capsule.Id).Value);

            var reset = service.ResetClockBaseline();

            Assert.Equal(SealServiceStatus.Ok, reset.Status);
            Assert.Equal(Now, reset.Value);
            Assert.True(service.CanTake(capsule.Id).Value);
            Assert.Equal(Now, service.GetObservedMaxUtc().Value);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task UsageCountsSplitCapsulesAndMeasureTheContainers()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var store = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(Password, out var vmk, out _));

        try
        {
            var ordinary = WriteEntry(paths, vmk, "ordinary.bin", 64);
            var capsule = WriteEntry(paths, vmk, "capsule.bin", 64);

            SealIndex.Empty
                .WithItem(Item(ordinary.Id, ordinary.Plain, "ordinary.bin"))
                .WithItem(Item(capsule.Id, capsule.Plain, "capsule.bin", capsule: true, expiry: Now + 30 * 86_400))
                .Save(paths.Index, vmk);

            var usage = service.GetUsage();
            Assert.Equal(SealServiceStatus.Ok, usage.Status);
            Assert.Equal(2, usage.Value.Total);
            Assert.Equal(1, usage.Value.Space);
            Assert.Equal(1, usage.Value.Capsules);


            var onDisk = new FileInfo(paths.ItemPath(ordinary.Id)).Length
                + new FileInfo(paths.ItemPath(capsule.Id)).Length;
            Assert.Equal(onDisk, usage.Value.UsedBytes);
            Assert.True(usage.Value.UsedBytes > 64);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task SourceAlreadyGoneNeverDeletesTheOriginal()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = dir.PathOf("keep.txt");
        await File.WriteAllTextAsync(source, "keep me");

        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var store = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(Password, out var vmk, out _));

        try
        {

            var item = Item(NewId(), Encoding.UTF8.GetBytes("keep me"), "keep.txt");
            item.SourcePath = source;
            SealIndex.Empty.WithItem(item).WithObservedMax(Now).Save(paths.Index, vmk);

            const string opId = "0123456789abcdef0123456789abcdef";
            var journal = SealJournal.Empty.Append(new SealJournalRecord
            {
                OpId = opId,
                Op = SealJournalOp.Seal,
                Phase = SealJournalPhase.SealBegin,
                ItemId = item.Id,
                SourcePath = source,
                StartedAtUtc = Now,
            });
            journal.Save(paths.Journal, vmk);

            var index = SealIndex.Load(paths.Index, vmk);
            var handled = await SealRecovery.ResolveAsync(
                index, journal, paths, vmk,
                new SealRecoveryAction(SealRecoveryActionKind.OrphanSourceRemains, opId, item.Id),
                SealRecoveryChoice.SourceAlreadyGone,
                new SealFileProbe(paths, index));


            Assert.False(handled);
            Assert.True(File.Exists(source));
            Assert.Single(SealJournal.Load(paths.Journal, vmk).Records);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task ACleanSpacePlansNoRecoveryWork()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var plan = service.GetRecoveryPlan();
        Assert.Equal(SealServiceStatus.Ok, plan.Status);
        Assert.Empty(plan.Value!.Actions);
        Assert.Equal(SealJournalStatus.Ok, plan.Value.JournalStatus);



        var unknown = await service.ApplyRecoveryAsync(
            new SealRecoveryAction(SealRecoveryActionKind.None, "nope", "nope"), SealRecoveryChoice.KeepBoth);
        Assert.Equal(SealServiceStatus.InvalidRequest, unknown);
    }

    [Fact]
    public async Task AMismatchedChoiceDoesNotKeepTheWriteGate()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));



        Assert.Equal(SealServiceStatus.InvalidRequest, await service.ApplyRecoveryAsync(
            new SealRecoveryAction(SealRecoveryActionKind.None, "nope", "nope"), SealRecoveryChoice.KeepBoth));

        Assert.NotEqual(SealServiceStatus.Busy, service.DiscardMalformedJournalRecords().Status);
    }

    [Fact]
    public async Task AHintGoesRoundAndClearsWithoutAKey()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        Assert.Equal(SealServiceStatus.Ok, await service.WriteHintAsync("猫的名字 + 生日"));
        var read = service.ReadHint();
        Assert.Equal(SealServiceStatus.Ok, read.Status);
        Assert.Equal("猫的名字 + 生日", read.Value);


        Assert.Equal(SealServiceStatus.Ok, await service.WriteHintAsync(null));
        Assert.False(File.Exists(paths.Hint));
        Assert.Null(service.ReadHint().Value);



        Assert.Equal(SealServiceStatus.Ok, await service.WriteHintAsync("再看一眼"));
        using var locked = NewService(paths);
        Assert.False(locked.IsUnlocked);
        Assert.Equal("再看一眼", locked.ReadHint().Value);
    }

    [Fact]
    public async Task ADirectSessionCannotDoThePrivilegedFour()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password, SealSessionMode.Direct));

        Assert.Equal(SealServiceStatus.RequiresFullSession, await service.ChangePasswordAsync(Password, "Other-9-tunnel-42"));
        Assert.Equal(SealServiceStatus.RequiresFullSession, await service.ExportAsync(dir.PathOf("x.novabundle"), Password));
        Assert.Equal(SealServiceStatus.RequiresFullSession, (await service.ImportAsync(dir.PathOf("x.novabundle"), Password)).Status);
        Assert.Equal(SealServiceStatus.RequiresFullSession, await service.ClearAsync());

        Assert.Equal(SealServiceStatus.RequiresFullSession, await service.ClearWithoutSessionAsync());

        Assert.Equal(SealServiceStatus.RequiresFullSession,
            await service.ExtractBundleBackupAsync(dir.PathOf("x.novabundle"), Password, dir.PathOf("backup.novaenc")));


        Assert.Equal(SealServiceStatus.Ok, service.ListItems().Status);
    }

    [Fact]
    public async Task AnImportRefusesWhileTheJournalNeedsRecovery()
    {

        using var dir = new TempDir();
        var paths = NewPaths(dir);

        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));


        var bundle = dir.PathOf("good.novabundle");
        Assert.Equal(SealServiceStatus.Ok, await service.ExportAsync(bundle, Password));


        File.WriteAllBytes(paths.Journal, [0x00, 0x11, 0x22, 0x33]);
        var call = await service.ImportAsync(bundle, Password);
        Assert.Equal(SealServiceStatus.Corrupt, call.Status);
    }

    [Fact]
    public async Task AnExportRefusesWhileARecoveryDecisionIsPending()
    {






        using var dir = new TempDir();
        var paths = NewPaths(dir);

        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));


        var seed = dir.PathOf("seed.txt");
        File.WriteAllBytes(seed, [1, 2, 3]);
        var sealedResult = await service.SealAsync([new SealRequest(seed)]);
        Assert.Equal(SealServiceStatus.Ok, sealedResult.Status);
        var itemId = Assert.Single(sealedResult.Value!).ItemId;
        Assert.False(File.Exists(seed));


        var orphan = dir.PathOf("orphan-source.txt");
        File.WriteAllBytes(orphan, [4, 5, 6]);


        Assert.True(SealKeyWrap.TryUnwrap(File.ReadAllBytes(paths.KeyWrap), Password, out var vmk, out _));
        var record = new SealJournalRecord
        {
            OpId = NewId(),
            Op = SealJournalOp.Seal,
            Phase = SealJournalPhase.SealIndexed,
            ItemId = itemId,
            SourcePath = orphan,
            StartedAtUtc = Now,
            Draft = new SealJournalDraft
            {
                OriginalName = "seed.txt",
                DisplayName = "seed.txt",
                Size = 3,
                SealedAtUtc = Now,
                ContentSha256 = Convert.ToHexString(SHA256.HashData([1, 2, 3])).ToLowerInvariant(),
            },
        };
        SealJournal.Empty.Append(record).Save(paths.Journal, vmk);
        CryptographicOperations.ZeroMemory(vmk);


        Assert.Equal(SealServiceStatus.Corrupt, await service.ExportAsync(dir.PathOf("refused.novabundle"), Password));
        Assert.Equal(SealServiceStatus.Corrupt, (await service.ImportAsync(dir.PathOf("never.novabundle"), Password)).Status);
    }






    [Fact]
    public async Task ATakeRefusesWhileTheJournalIsUnreadable()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var source = dir.PathOf("take-me.txt");
        File.WriteAllBytes(source, [1, 2, 3]);
        var sealedResult = await service.SealAsync([new SealRequest(source)]);
        Assert.Equal(SealServiceStatus.Ok, sealedResult.Status);
        var itemId = Assert.Single(sealedResult.Value!).ItemId;

        File.WriteAllBytes(paths.Journal, [0x00, 0x11, 0x22, 0x33]);

        var outDir = dir.PathOf("out");
        Directory.CreateDirectory(outDir);
        var result = await service.TakeAsync(new SealTakeRequest(itemId, outDir));
        Assert.Equal(SealServiceStatus.Corrupt, result.Status);

        Assert.True(File.Exists(paths.ItemPath(itemId)));
        Assert.False(File.Exists(Path.Combine(outDir, "take-me.txt")));
    }

    [Fact]
    public async Task AFailedImportToANewSpaceRollsBackToUninitialized()
    {


        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.False(service.IsInitialized());

        var junk = dir.PathOf("junk.novabundle");
        File.WriteAllBytes(junk, [0x00, 0x11, 0x22, 0x33]);
        var call = await service.ImportAsync(junk, Password, "Local-9-tunnel-42");
        Assert.NotEqual(SealServiceStatus.Ok, call.Status);


        Assert.False(service.IsInitialized());
        Assert.False(File.Exists(paths.KeyWrap));
    }

    [Fact]
    public void AWeakenedKeywrapIsReportedOnUnlock()
    {


        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var store = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.Initialize(Password, null, out _));
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(Password, out var vmk, out var weakened));
        Assert.True(weakened);
        CryptographicOperations.ZeroMemory(vmk);
    }

    [Fact]
    public async Task ConsumeUnlockWeakened_IsOneShot()
    {

        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);

        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.False(service.ConsumeUnlockWeakened());

        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));
        Assert.True(service.ConsumeUnlockWeakened());
        Assert.False(service.ConsumeUnlockWeakened());

        service.Lock();
        Assert.False(service.ConsumeUnlockWeakened());

        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));
        Assert.True(service.ConsumeUnlockWeakened());
    }

    [Fact]
    public async Task LockEndsTheSession()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        Assert.Equal(SealServiceStatus.Ok, service.ListItems().Status);

        service.Lock();
        service.Lock();

        Assert.Equal(SealServiceStatus.NotUnlocked, service.Status());
        Assert.Equal(SealServiceStatus.NotUnlocked, service.ListItems().Status);
        Assert.False(service.IsUnlocked);
    }

    [Fact]
    public async Task TwoConcurrentTakesOfOneEntryLeaveOneBusy()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var store = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(Password, out var vmk, out _));

        string id;
        try
        {

            var entry = WriteEntry(paths, vmk, "big.bin", 4 * 1024 * 1024);
            id = entry.Id;

            Seed(paths, vmk, Item(entry.Id, entry.Plain, "big.bin"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }

        var target = dir.PathOf("out");
        Directory.CreateDirectory(target);

        var first = service.TakeAsync(new SealTakeRequest(id, target));
        var second = service.TakeAsync(new SealTakeRequest(id, target));

        var results = await Task.WhenAll(first, second);

        Assert.Single(results, r => r.Status == SealServiceStatus.Ok);
        Assert.Single(results, r => r.Status == SealServiceStatus.Busy);

        var busy = results.Single(r => r.Status == SealServiceStatus.Busy);
        Assert.Equal(SealTakeStatus.Busy, busy.Value!.Status);
    }

    [Fact]
    public async Task EveryWriterRefusesWhileAnotherWriteOwnsTheSpace()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var id = NewId();
        var target = dir.PathOf("out");
        Directory.CreateDirectory(target);



        await service.WriteGate.WaitAsync();
        try
        {
            Assert.Equal(SealServiceStatus.Busy,
                (await service.SealAsync([new SealRequest(dir.PathOf("x.bin"))])).Status);

            var take = await service.TakeAsync(new SealTakeRequest(id, target));
            Assert.Equal(SealServiceStatus.Busy, take.Status);
            Assert.Equal(SealTakeStatus.Busy, take.Value!.Status);

            Assert.Equal(SealServiceStatus.Busy, await service.DestroyAsync(id));

            Assert.Equal(SealServiceStatus.Busy, await service.ApplyRecoveryAsync(
                new SealRecoveryAction(SealRecoveryActionKind.OrphanSourceRemains, "op", id),
                SealRecoveryChoice.TryDeleteSource));

            Assert.Equal(SealServiceStatus.Busy,
                (await service.ImportAsync(dir.PathOf("x.novabundle"), Password)).Status);



            Assert.Equal(SealServiceStatus.Busy,
                await service.ChangePasswordAsync(Password, "Other-9-tunnel-42"));


            Assert.Equal(SealServiceStatus.Busy, await service.WriteHintAsync("x"));

            Assert.Equal(SealServiceStatus.Busy, await service.ClearAsync());
        }
        finally
        {
            service.WriteGate.Release();
        }


        Assert.Equal(SealServiceStatus.Ok, await service.WriteHintAsync("y"));


        Assert.Equal(SealServiceStatus.Ok, await service.ClearAsync());
        Assert.False(service.IsUnlocked);
        Assert.False(new SealSpaceStore(paths).Exists());
    }

    [Fact]
    public async Task TryLockIfEpochOnlyEndsTheMatchingSession()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));

        var (status, epoch) = await service.UnlockWithEpochAsync(Password);
        Assert.Equal(SealServiceStatus.Ok, status);
        Assert.True(service.IsUnlocked);

        Assert.False(service.TryLockIfEpoch(epoch - 1));
        Assert.True(service.IsUnlocked);

        Assert.True(service.TryLockIfEpoch(epoch));
        Assert.False(service.IsUnlocked);
    }

    [Fact]
    public async Task ClearingALockedSpaceStillRefusesWhileAWriteIsInFlight()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));



        service.Lock();
        Assert.False(service.IsUnlocked);

        await service.WriteGate.WaitAsync();
        try
        {
            Assert.Equal(SealServiceStatus.Busy, await service.ClearWithoutSessionAsync());

            Assert.True(new SealSpaceStore(paths).Exists());
        }
        finally
        {
            service.WriteGate.Release();
        }

        Assert.Equal(SealServiceStatus.Ok, await service.ClearWithoutSessionAsync());
        Assert.False(new SealSpaceStore(paths).Exists());
    }

    [Fact]
    public async Task AnAutomaticRecoveryPassWaitsForAWriteInFlight()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        await service.WriteGate.WaitAsync();
        var recovery = service.RunAutomaticRecoveryAsync();
        try
        {

            await Task.Delay(250);
            Assert.False(recovery.IsCompleted);
        }
        finally
        {
            service.WriteGate.Release();
        }

        var run = await recovery;
        Assert.Equal(SealServiceStatus.Ok, run.Status);
    }

    [Fact]
    public async Task ALockDuringASealDoesNotZeroTheKeyUnderTheWriter()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));


        var source = dir.PathOf("big.bin");
        var plaintext = new byte[32 * 1024 * 1024];
        new Random(7).NextBytes(plaintext);
        await File.WriteAllBytesAsync(source, plaintext);

        var seal = service.SealAsync([new SealRequest(source)]);
        service.Lock();
        var sealedResult = await seal;

        Assert.Equal(SealServiceStatus.Ok, sealedResult.Status);
        var one = Assert.Single(sealedResult.Value!);
        Assert.True(one.Committed);
        Assert.Equal(SealSealError.None, one.Error);


        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));
        var items = service.ListItems();
        Assert.Equal(SealServiceStatus.Ok, items.Status);
        var item = Assert.Single(items.Value!);

        var target = dir.PathOf("out");
        Directory.CreateDirectory(target);
        var take = await service.TakeAsync(new SealTakeRequest(item.Id, target));
        Assert.Equal(SealServiceStatus.Ok, take.Status);
        Assert.Equal(SealTakeStatus.Taken, take.Value!.Status);

        var restored = await File.ReadAllBytesAsync(Path.Combine(target, item.OriginalName));
        Assert.Equal(plaintext, restored);
    }

    [Fact]
    public async Task ANewerIndexVersionIsReportedNotThrown()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));


        var bytes = File.ReadAllBytes(paths.Index);
        bytes[8] = 2;
        File.WriteAllBytes(paths.Index, bytes);




        Assert.Equal(SealServiceStatus.VersionUnsupported, service.ListItems().Status);
        Assert.Equal(SealServiceStatus.VersionUnsupported, service.CanTake(NewId()).Status);
        Assert.Equal(SealServiceStatus.VersionUnsupported, await service.DestroyAsync(NewId()));
        Assert.Equal(SealServiceStatus.VersionUnsupported, await service.ExportAsync(dir.PathOf("x.novabundle"), Password));
    }

    [Fact]
    public async Task TheOrphanReminderSettlesWithEitherAnswer()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var store = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(Password, out var vmk, out _));

        try
        {

            var source = dir.PathOf("still-there.txt");
            await File.WriteAllTextAsync(source, "one copy in the box, one outside");
            var plain = Encoding.UTF8.GetBytes("one copy in the box, one outside");
            var orphan = Item(NewId(), plain, "still-there.txt");
            orphan.SourcePath = source;

            const string OpDelete = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            SealIndex.Empty.WithItem(orphan).WithObservedMax(Now).Save(paths.Index, vmk);
            SealJournal.Empty.Append(new SealJournalRecord
            {
                OpId = OpDelete,
                Op = SealJournalOp.Seal,
                Phase = SealJournalPhase.SealIndexed,
                ItemId = orphan.Id,
                SourcePath = source,
                StartedAtUtc = Now,
                Draft = Draft(plain, "still-there.txt"),
            }).Save(paths.Journal, vmk);

            Assert.Equal(SealServiceStatus.Ok, await service.ApplyRecoveryAsync(
                new SealRecoveryAction(SealRecoveryActionKind.OrphanSourceRemains, OpDelete, orphan.Id),
                SealRecoveryChoice.TryDeleteSource));

            Assert.False(File.Exists(source));
            Assert.Empty(SealJournal.Load(paths.Journal, vmk).Records);

            Assert.NotNull(SealIndex.Load(paths.Index, vmk).Find(orphan.Id));


            var gonePlain = Encoding.UTF8.GetBytes("the user removed the copy outside");
            var gone = Item(NewId(), gonePlain, "already-gone.txt");
            gone.SourcePath = dir.PathOf("already-gone.txt");

            const string OpGone = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            SealIndex.Load(paths.Index, vmk).WithItem(gone).WithObservedMax(Now).Save(paths.Index, vmk);
            SealJournal.Empty.Append(new SealJournalRecord
            {
                OpId = OpGone,
                Op = SealJournalOp.Seal,
                Phase = SealJournalPhase.SealIndexed,
                ItemId = gone.Id,
                SourcePath = gone.SourcePath,
                StartedAtUtc = Now,
                Draft = Draft(gonePlain, "already-gone.txt"),
            }).Save(paths.Journal, vmk);

            Assert.Equal(SealServiceStatus.Ok, await service.ApplyRecoveryAsync(
                new SealRecoveryAction(SealRecoveryActionKind.OrphanSourceRemains, OpGone, gone.Id),
                SealRecoveryChoice.SourceAlreadyGone));

            Assert.Empty(SealJournal.Load(paths.Journal, vmk).Records);
            Assert.NotNull(SealIndex.Load(paths.Index, vmk).Find(gone.Id));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task BothSidesOfferThreeSettlements()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var store = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(Password, out var vmk, out _));

        try
        {
            async Task<(SealServiceStatus Status, bool Entry, bool Target, bool Container)> Settle(
                SealRecoveryChoice choice)
            {
                const string Op = "cccccccccccccccccccccccccccccccc";
                var id = NewId();
                var target = dir.PathOf("target-" + id + ".txt");
                await File.WriteAllTextAsync(target, "the plaintext that was already written");
                var plain = Encoding.UTF8.GetBytes("the plaintext that was already written");

                var item = Item(id, plain, "left-both-sides.txt");
                File.WriteAllBytes(paths.ItemPath(id), [1, 2, 3, 4]);
                SealIndex.Empty.WithItem(item).WithObservedMax(Now).Save(paths.Index, vmk);
                SealJournal.Empty.Append(new SealJournalRecord
                {
                    OpId = Op,
                    Op = SealJournalOp.Take,
                    Phase = SealJournalPhase.TakeBegin,
                    ItemId = id,
                    SourcePath = dir.PathOf("left-both-sides.txt"),
                    TargetPath = target,
                    StartedAtUtc = Now,
                }).Save(paths.Journal, vmk);

                var status = await service.ApplyRecoveryAsync(
                    new SealRecoveryAction(SealRecoveryActionKind.BothSidesExist, Op, id), choice);

                Assert.Empty(SealJournal.Load(paths.Journal, vmk).Records);
                return (status,
                    SealIndex.Load(paths.Index, vmk).Find(id) is not null,
                    File.Exists(target),
                    File.Exists(paths.ItemPath(id)));
            }


            var keepTarget = await Settle(SealRecoveryChoice.KeepTarget);
            Assert.Equal(SealServiceStatus.Ok, keepTarget.Status);
            Assert.False(keepTarget.Entry);
            Assert.True(keepTarget.Target);
            Assert.False(keepTarget.Container);


            var keepBox = await Settle(SealRecoveryChoice.KeepBoxEntry);
            Assert.Equal(SealServiceStatus.Ok, keepBox.Status);
            Assert.True(keepBox.Entry);
            Assert.False(keepBox.Target);
            Assert.True(keepBox.Container);


            var keepBoth = await Settle(SealRecoveryChoice.KeepBoth);
            Assert.Equal(SealServiceStatus.Ok, keepBoth.Status);
            Assert.True(keepBoth.Entry);
            Assert.True(keepBoth.Target);
            Assert.True(keepBoth.Container);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task APartialTargetIsCleanedOnlyWhenAsked()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var store = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(Password, out var vmk, out _));

        try
        {
            async Task<(SealServiceStatus Status, bool Entry, bool Target)> Settle(SealRecoveryChoice choice)
            {
                const string Op = "dddddddddddddddddddddddddddddddd";
                var id = NewId();
                var target = dir.PathOf("partial-" + id + ".txt");
                await File.WriteAllTextAsync(target, "only half of it got written");
                var plain = Encoding.UTF8.GetBytes("only half of it got written");

                var item = Item(id, plain, "half-written.txt");
                File.WriteAllBytes(paths.ItemPath(id), [1, 2, 3, 4]);
                SealIndex.Empty.WithItem(item).WithObservedMax(Now).Save(paths.Index, vmk);
                SealJournal.Empty.Append(new SealJournalRecord
                {
                    OpId = Op,
                    Op = SealJournalOp.Take,
                    Phase = SealJournalPhase.TakeBegin,
                    ItemId = id,
                    SourcePath = dir.PathOf("half-written.txt"),
                    TargetPath = target,
                    StartedAtUtc = Now,
                }).Save(paths.Journal, vmk);

                var status = await service.ApplyRecoveryAsync(
                    new SealRecoveryAction(SealRecoveryActionKind.AskCleanupPartialTarget, Op, id), choice);

                Assert.Empty(SealJournal.Load(paths.Journal, vmk).Records);
                return (status, SealIndex.Load(paths.Index, vmk).Find(id) is not null, File.Exists(target));
            }

            var cleaned = await Settle(SealRecoveryChoice.CleanupPartialTarget);
            Assert.Equal(SealServiceStatus.Ok, cleaned.Status);
            Assert.True(cleaned.Entry);
            Assert.False(cleaned.Target);


            var kept = await Settle(SealRecoveryChoice.KeepPartialTarget);
            Assert.Equal(SealServiceStatus.Ok, kept.Status);
            Assert.True(kept.Entry);
            Assert.True(kept.Target);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task TheAutomaticPassAppliesWhatItCanAndSweepsFragments()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var store = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(Password, out var vmk, out _));

        try
        {


            var source = dir.PathOf("original-intact.txt");
            await File.WriteAllTextAsync(source, "the original never moved");
            var halfId = NewId();
            File.WriteAllBytes(paths.SealingPath(halfId), [9, 9, 9]);

            const string OpFragment = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
            var journal = SealJournal.Empty.Append(new SealJournalRecord
            {
                OpId = OpFragment,
                Op = SealJournalOp.Seal,
                Phase = SealJournalPhase.SealBegin,
                ItemId = halfId,
                SourcePath = source,
                StartedAtUtc = Now,
            });


            const string OpTake = "ffffffffffffffffffffffffffffffff";
            var takenId = NewId();
            journal = journal.Append(new SealJournalRecord
            {
                OpId = OpTake,
                Op = SealJournalOp.Take,
                Phase = SealJournalPhase.Taken,
                ItemId = takenId,
                SourcePath = dir.PathOf("taken-original.txt"),
                TargetPath = dir.PathOf("taken-target.txt"),
                StartedAtUtc = Now,
            });
            journal.Save(paths.Journal, vmk);


            var orphanContainer = paths.ItemPath(NewId());
            File.WriteAllBytes(orphanContainer, [5, 5, 5]);

            var run = await service.RunAutomaticRecoveryAsync();

            Assert.Equal(SealServiceStatus.Ok, run.Status);
            Assert.Equal(2, run.Value!.Applied);
            Assert.Empty(run.Value.Deferred);
            Assert.Equal(1, run.Value.SweptContainers);
            Assert.False(File.Exists(paths.SealingPath(halfId)));
            Assert.False(File.Exists(orphanContainer));

            Assert.Single(SealJournal.Load(paths.Journal, vmk).Records);
            var entries = service.GetRecoveryEntries();
            Assert.Equal(SealRecoveryActionKind.OfferRetrySeal, Assert.Single(entries.Value!).Action.Kind);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task TheAutomaticPassDefersWhatItCannotFinish()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var store = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(Password, out var vmk, out _));

        try
        {


            var plain = Encoding.UTF8.GetBytes("the container no longer exists");
            var id = NewId();
            const string OpLost = "abababababababababababababababab";

            SealIndex.Empty.WithObservedMax(Now).Save(paths.Index, vmk);
            SealJournal.Empty.Append(new SealJournalRecord
            {
                OpId = OpLost,
                Op = SealJournalOp.Seal,
                Phase = SealJournalPhase.Sealed,
                ItemId = id,
                SourcePath = dir.PathOf("no-longer-here.txt"),
                StartedAtUtc = Now,
                Draft = Draft(plain, "no-longer-here.txt"),
            }).Save(paths.Journal, vmk);

            var plan = service.GetRecoveryPlan();
            Assert.Equal(SealServiceStatus.Ok, plan.Status);
            Assert.Equal(SealRecoveryActionKind.CompleteSealFromJournal, Assert.Single(plan.Value!.Actions).Kind);

            Assert.Empty(service.GetRecoveryEntries().Value!);

            var run = await service.RunAutomaticRecoveryAsync();

            Assert.Equal(SealServiceStatus.Ok, run.Status);
            Assert.Equal(0, run.Value!.Applied);
            Assert.Equal(SealRecoveryActionKind.CompleteSealFromJournal, Assert.Single(run.Value.Deferred).Kind);

            Assert.Single(SealJournal.Load(paths.Journal, vmk).Records);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task TheAutomaticPassReportsOnlyAutomaticFailures()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));

        var store = new SealSpaceStore(paths, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(Password, out var vmk, out _));

        try
        {

            var source = dir.PathOf("orphan-source.txt");
            await File.WriteAllTextAsync(source, "still on disk");
            var orphanPlain = Encoding.UTF8.GetBytes("still on disk");
            var orphan = Item(NewId(), orphanPlain, "orphan-source.txt");
            orphan.SourcePath = source;

            const string OrphanOp = "11111111111111111111111111111111";
            var journal = SealJournal.Empty.Append(new SealJournalRecord
            {
                OpId = OrphanOp,
                Op = SealJournalOp.Seal,
                Phase = SealJournalPhase.SealIndexed,
                ItemId = orphan.Id,
                SourcePath = source,
                StartedAtUtc = Now,
                Draft = Draft(orphanPlain, "orphan-source.txt"),
            });


            var lostPlain = Encoding.UTF8.GetBytes("container gone");
            var lostId = NewId();
            const string LostOp = "22222222222222222222222222222222";
            journal = journal.Append(new SealJournalRecord
            {
                OpId = LostOp,
                Op = SealJournalOp.Seal,
                Phase = SealJournalPhase.Sealed,
                ItemId = lostId,
                SourcePath = dir.PathOf("lost-source.txt"),
                StartedAtUtc = Now,
                Draft = Draft(lostPlain, "lost-source.txt"),
            });

            SealIndex.Empty.WithItem(orphan).WithObservedMax(Now).Save(paths.Index, vmk);
            journal.Save(paths.Journal, vmk);

            var run = await service.RunAutomaticRecoveryAsync();

            Assert.Equal(SealServiceStatus.Ok, run.Status);
            Assert.Equal(0, run.Value!.Applied);

            var deferred = Assert.Single(run.Value.Deferred);
            Assert.Equal(SealRecoveryActionKind.CompleteSealFromJournal, deferred.Kind);

            var entries = service.GetRecoveryEntries();
            Assert.Equal(SealServiceStatus.Ok, entries.Status);
            Assert.Equal(SealRecoveryActionKind.OrphanSourceRemains, Assert.Single(entries.Value!).Action.Kind);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task ADamagedJournalIsReportedAndTheSweepStopsGuessing()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        using var service = NewService(paths);
        Assert.Equal(SealServiceStatus.Ok, await service.InitializeAsync(Password));
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(Password));


        var evidence = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(paths.Journal, evidence);

        var orphanContainer = paths.ItemPath(NewId());
        File.WriteAllBytes(orphanContainer, [6, 6, 6]);

        var plan = service.GetRecoveryPlan();
        Assert.Equal(SealServiceStatus.Ok, plan.Status);
        Assert.Equal(SealJournalStatus.Unreadable, plan.Value!.JournalStatus);
        Assert.True(plan.Value.JournalNeedsAttention);
        Assert.Empty(plan.Value.Actions);
        Assert.Empty(service.GetRecoveryEntries().Value!);

        var run = await service.RunAutomaticRecoveryAsync();
        Assert.Equal(SealServiceStatus.Ok, run.Status);
        Assert.Equal(SealJournalStatus.Unreadable, run.Value!.JournalStatus);

        Assert.Equal(0, run.Value.SweptContainers);
        Assert.True(File.Exists(orphanContainer));

        Assert.Equal(evidence, File.ReadAllBytes(paths.Journal));
    }

    [Fact]
    public void NoPublicMemberHandsOutTheKey()
    {
        var surface = typeof(SealService).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.DeclaringType == typeof(SealService))
            .Select(m => m.ReturnType)
            .Concat(typeof(SealService).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(p => p.DeclaringType == typeof(SealService))
                .Select(p => p.PropertyType))
            .ToList();

        foreach (var type in surface)
        {
            Assert.NotEqual(typeof(byte[]), type);
            Assert.False(
                type.IsGenericType && type.GetGenericArguments().Any(a => a == typeof(byte[])),
                $"{type} exposes a key buffer");
        }
    }



    private static SealJournalDraft Draft(byte[] plaintext, string name)
        => new()
        {
            OriginalName = name,
            DisplayName = name,
            Size = (ulong)plaintext.Length,
            SealedAtUtc = Now,
            Kind = SealedItemKind.Normal,
            ContentSha256 = Sha(plaintext),
        };

    private static string Sha(byte[] plaintext)
        => Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant();
}
