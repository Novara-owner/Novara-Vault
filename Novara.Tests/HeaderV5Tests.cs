using System.Security.Cryptography;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class HeaderV5Tests : IDisposable
{
    private const string LockPassword = "lock-pw-v5";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "novara-v5-" + Guid.NewGuid().ToString("N"));

    public HeaderV5Tests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        PasswordService.SetBaseDir(null);
        try { Directory.Delete(_root, true); } catch { }
    }

    private NovaraStore NewV5Store(string fileName)
    {
        PasswordService.SetBaseDir(_root);


        if (!File.Exists(Path.Combine(_root, "security.dat"))) Assert.True(PasswordService.Create(LockPassword));
        var store = new NovaraStore(Path.Combine(_root, fileName));
        store.Load();
        Assert.True(store.EnableEncryption(LockPassword));
        Assert.Equal(NovaraStore.FileVersionMainV5, store.FileVersionForTests);
        return store;
    }

    private static void OverwriteFile(string path, byte[] bytes)
    {

        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllBytes(path, bytes);
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.ReadOnly);
    }

    private static void AddMemo(NovaraStore store, string name)
    {
        store.Database.MemoEntries.Add(new MemoEntry { Name = name, Type = "自定义" });
        Assert.True(store.SaveSync());
    }



    [Fact]
    public void Argon2id_ReproducesTheRfc9106TestVector()
    {


        var password = Enumerable.Repeat((byte)0x01, 32).ToArray();
        var salt = Enumerable.Repeat((byte)0x02, 16).ToArray();
        var secret = Enumerable.Repeat((byte)0x03, 8).ToArray();
        var ad = Enumerable.Repeat((byte)0x04, 12).ToArray();
        var expected = Convert.FromHexString("0D640DF58D78766C08C037A34A8B53C9D01EF0452D75B65EB52520E96B01E659");

        var actual = Argon2idVector(password, salt, secret, ad, 32, 3, 4);
        Assert.True(expected.AsSpan().SequenceEqual(actual),
            "RFC 9106 向量不符：实际 = " + Convert.ToHexString(actual));
    }


    private static byte[] Argon2idVector(byte[] password, byte[] salt, byte[] secret, byte[] ad, int memoryKiB, int iterations, int lanes)
    {
        using var argon2 = new Konscious.Security.Cryptography.Argon2id(password)
        {
            Salt = salt,
            Iterations = iterations,
            MemorySize = memoryKiB,
            DegreeOfParallelism = lanes,
            KnownSecret = secret,
            AssociatedData = ad,
        };
        return argon2.GetBytes(32);
    }



    [Fact]
    public void V5WriteClamp_BoundsArgon2idButLeavesPbkdf2Alone()
    {
        Assert.Equal((10, 512 * 1024),
            NovaraStore.ClampV5ParamsForWrite(CryptoService.V5KdfArgon2id, 100, 4 * 1024 * 1024));
        Assert.Equal((3, 64 * 1024),
            NovaraStore.ClampV5ParamsForWrite(CryptoService.V5KdfArgon2id, 3, 64 * 1024));
        Assert.Equal((3_000_000, 0),
            NovaraStore.ClampV5ParamsForWrite(CryptoService.V5KdfPbkdf2Sha256, 3_000_000, 0));
    }

    [Fact]
    public void V5ReadGate_RejectsAHeaderThatNeedsMoreMemoryThanTheMachineHas()
    {
        NewV5Store("v5-too-expensive.novadb");
        var path = Path.Combine(_root, "v5-too-expensive.novadb");
        var bytes = File.ReadAllBytes(path);
        BitConverter.TryWriteBytes(bytes.AsSpan(11, 4), 4 * 1024 * 1024);
        OverwriteFile(path, bytes);

        NovaraStore.AvailableMemoryBytesForTests = 1L << 30;
        try
        {
            var reopened = new NovaraStore(path);
            Assert.Equal(LoadStatus.Encrypted, reopened.Load().Status);
            Assert.Equal(LoadStatus.KdfTooExpensive, reopened.LoadWithPassword(LockPassword).Status);
        }
        finally
        {
            NovaraStore.AvailableMemoryBytesForTests = 0;
        }
    }

    [Fact]
    public void V5Save_ReKeysAHighCostHeaderDownToTheWriteCeiling()
    {
        var store = NewV5Store("v5-clamp.novadb");
        AddMemo(store, "clamp");
        store.SetV5KdfParamsForTests(100, 1024 * 1024);
        Assert.True(store.SaveSync());

        var path = Path.Combine(_root, "v5-clamp.novadb");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(10, BitConverter.ToInt32(bytes.AsSpan(7, 4)));
        Assert.Equal(512 * 1024, BitConverter.ToInt32(bytes.AsSpan(11, 4)));

        var reopened = new NovaraStore(path);
        Assert.Equal(LoadStatus.Ok, reopened.LoadWithPassword(LockPassword).Status);
    }



    [Fact]
    public void V5RoundTrip_SecondSaveAndCrossInstanceUnlock_PreserveData()
    {
        var store = NewV5Store("v5-a.novadb");
        AddMemo(store, "v5-entry");

        var bytes = File.ReadAllBytes(Path.Combine(_root, "v5-a.novadb"));
        Assert.Equal((byte)5, bytes[4]);
        Assert.Equal((byte)0x01, bytes[6]);
        Assert.Equal((byte)0x01, bytes[5]);
        Assert.True(bytes.Length >= NovaraStore.HeaderSizeV5);

        AddMemo(store, "v5-entry-2");
        Assert.Equal(NovaraStore.FileVersionMainV5, store.FileVersionForTests);

        var reopened = new NovaraStore(Path.Combine(_root, "v5-a.novadb"));
        Assert.Equal(LoadStatus.Encrypted, reopened.Load().Status);
        Assert.Equal(LoadStatus.Ok, reopened.LoadWithPassword(LockPassword).Status);
        Assert.Equal(2, reopened.Database.MemoEntries.Count);
        Assert.Contains(reopened.Database.MemoEntries, e => e.Name == "v5-entry-2");
    }

    [Fact]
    public void V5_IntegrityPrecheck_CatchesCiphertextTampering()
    {
        var store = NewV5Store("v5-tamper-body.novadb");
        AddMemo(store, "tamper");
        var path = Path.Combine(_root, "v5-tamper-body.novadb");

        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0x01;
        OverwriteFile(path, bytes);

        var reopened = new NovaraStore(path);
        Assert.Equal(LoadStatus.Encrypted, reopened.Load().Status);
        Assert.Equal(LoadStatus.Corrupted, reopened.LoadWithPassword(LockPassword).Status);
    }

    [Fact]
    public void V5_AadBinding_CatchesHeaderSaltTampering()
    {
        var store = NewV5Store("v5-tamper-salt.novadb");
        AddMemo(store, "tamper-salt");
        var path = Path.Combine(_root, "v5-tamper-salt.novadb");

        var bytes = File.ReadAllBytes(path);
        bytes[15] ^= 0x01;
        OverwriteFile(path, bytes);

        var reopened = new NovaraStore(path);
        Assert.Equal(LoadStatus.Encrypted, reopened.Load().Status);

        Assert.Equal(LoadStatus.Corrupted, reopened.LoadWithPassword(LockPassword).Status);
    }

    [Fact]
    public void V2V3_Compat_DowngradedWriteReadsBack_AndMigratesToV5()
    {
        var store = NewV5Store("v5-compat.novadb");
        AddMemo(store, "compat-entry");

        store.SetFileVersionForTests(NovaraStore.FileVersionKdfHardened);
        Assert.True(store.SaveSync());
        Assert.Equal(NovaraStore.FileVersionKdfHardened, store.FileVersionForTests);

        var reopened = new NovaraStore(Path.Combine(_root, "v5-compat.novadb"));
        Assert.Equal(LoadStatus.Encrypted, reopened.Load().Status);
        Assert.Equal(LoadStatus.Ok, reopened.LoadWithPassword(LockPassword).Status);
        Assert.Contains(reopened.Database.MemoEntries, e => e.Name == "compat-entry");
        Assert.True(reopened.NeedsV5Migration);

        Assert.True(reopened.MigrateToV5());
        Assert.Equal(NovaraStore.FileVersionMainV5, reopened.FileVersionForTests);
        Assert.False(reopened.NeedsV5Migration);

        var final = new NovaraStore(Path.Combine(_root, "v5-compat.novadb"));
        Assert.Equal(LoadStatus.Ok, final.LoadWithPassword(LockPassword).Status);
        Assert.Contains(final.Database.MemoEntries, e => e.Name == "compat-entry");
    }

    [Fact]
    public void DisableEncryption_WritesV5Plain_AndRoundsTripsThroughLoad()
    {
        var store = NewV5Store("v5-plain.novadb");
        AddMemo(store, "plain-entry");
        Assert.True(store.DisableEncryption());
        Assert.Equal(NovaraStore.FileVersionMainV5, store.FileVersionForTests);

        var bytes = File.ReadAllBytes(Path.Combine(_root, "v5-plain.novadb"));
        Assert.Equal((byte)NovaraStore.FlagPlain, bytes[5]);

        Assert.True(bytes.AsSpan(6, 25).ToArray().All(b => b == 0), "明文 v5 头的 KDF 字段必须全零");

        var reopened = new NovaraStore(Path.Combine(_root, "v5-plain.novadb"));
        Assert.Equal(LoadStatus.Ok, reopened.Load().Status);
        Assert.Contains(reopened.Database.MemoEntries, e => e.Name == "plain-entry");
    }
}
