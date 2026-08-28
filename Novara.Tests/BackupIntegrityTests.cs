using System.Text.Json;
using Novara.Services;
using Xunit;

namespace Novara.Tests;



[Collection("CoreSequential")]
public class BackupIntegrityTests : IDisposable
{
    private readonly string _dir;

    public BackupIntegrityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "novara-bkp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private NovaraStore NewStoreWithData(string name)
    {
        var store = new NovaraStore(Path.Combine(_dir, "data-" + Guid.NewGuid().ToString("N") + ".novadb"));
        store.Load();
        store.Database.MemoEntries.Add(new Novara.Models.MemoEntry { Name = name, Type = "自定义" });
        return store;
    }

    private static (byte[] File, byte[] Body) ReadFile(string path)
    {
        var all = File.ReadAllBytes(path);
        return (all[..38], all[38..]);
    }

    [Fact]
    public void Export_WritesV3Sha256Header()
    {
        var store = NewStoreWithData("完整性条目");
        var backup = Path.Combine(_dir, "v3.novabak");
        Assert.True(store.ExportBackup(backup, false));

        var (header, body) = ReadFile(backup);
        Assert.Equal((uint)0x41564F4E, BitConverter.ToUInt32(header, 0)); // "NOVA"
        Assert.Equal(3, header[4]);                                       // v3 backup format
        Assert.Equal(0, header[5]);                                       // plaintext
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(body), header[6..38]);
    }

    [Fact]
    public void RoundTrip_V3Import_RestoresData()
    {
        var store = NewStoreWithData("往返条目");
        Assert.Single(store.Database.MemoEntries); 
        var backup = Path.Combine(_dir, "rt.novabak");
        Assert.True(store.ExportBackup(backup, false));

        var (header, body) = ReadFile(backup);
        var bodyJson = System.Text.Json.JsonDocument.Parse(body);
        Assert.True(bodyJson.RootElement.TryGetProperty("memoEntries", out var arr), "body missing memoEntries: " + body[..Math.Min(200, body.Length)]);
        Assert.Equal(1, arr.GetArrayLength()); 

        var fresh = new NovaraStore(Path.Combine(_dir, "fresh.novadb"));
        var result = fresh.ImportBackup(backup);
        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Single(fresh.Database.MemoEntries);
        Assert.Equal("往返条目", fresh.Database.MemoEntries[0].Name);
    }

    [Fact]
    public void Tampered_V3_Body_Rejected()
    {
        var store = NewStoreWithData("防篡改条目");
        var backup = Path.Combine(_dir, "tamper.novabak");
        Assert.True(store.ExportBackup(backup, false));

        var all = File.ReadAllBytes(backup);
        all[all.Length - 1] ^= 0xFF; // flip one body byte
        File.WriteAllBytes(backup, all);

        var fresh = new NovaraStore(Path.Combine(_dir, "t.novadb"));
        Assert.Equal(LoadStatus.Corrupted, fresh.ImportBackup(backup).Status);
    }

    [Fact]
    public void Legacy_V1_Backup_StillImportable()
    {
        
        var db = new Novara.Models.NovaraDatabase();
        db.MemoEntries.Add(new Novara.Models.MemoEntry { Name = "历史条目", Type = "自定义" });
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.SerializeToUtf8Bytes(db, opts);
        var legacy = Path.Combine(_dir, "legacy.novabak");
        using (var fs = File.Create(legacy))
        {
            var header = new byte[22];
            BitConverter.TryWriteBytes(header.AsSpan(0, 4), 0x41564F4E);
            header[4] = 1; header[5] = 0;
            System.Security.Cryptography.MD5.HashData(json).CopyTo(header, 6);
            fs.Write(header); fs.Write(json);
        }

        var fresh = new NovaraStore(Path.Combine(_dir, "l.novadb"));
        var result = fresh.ImportBackup(legacy);
        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Single(fresh.Database.MemoEntries);
        Assert.Equal("历史条目", fresh.Database.MemoEntries[0].Name);
    }

    [Fact]
    public void V3_EncryptedFlag_Rejected()
    {
        var store = NewStoreWithData("flag条目");
        var backup = Path.Combine(_dir, "flag.novabak");
        Assert.True(store.ExportBackup(backup, false));

        var all = File.ReadAllBytes(backup);
        all[5] = 0x01; // FlagEncrypted on a backup - not in the format matrix
        File.WriteAllBytes(backup, all);

        var fresh = new NovaraStore(Path.Combine(_dir, "f.novadb"));
        Assert.Equal(LoadStatus.Corrupted, fresh.ImportBackup(backup).Status);
    }

    [Fact]
    public void IncludeFilePathEntries_True_KeepsPaths()
    {
        var store = NewStoreWithData("路径往返条目");
        store.Database.PathBackupItems.Add(new Novara.Models.FilePathEntry { Name = "路径条目", Path = @"C:\some\path" });
        var backup = Path.Combine(_dir, "paths.novabak");
        Assert.True(store.ExportBackup(backup, true));

        var fresh = new NovaraStore(Path.Combine(_dir, "p.novadb"));
        Assert.Equal(LoadStatus.Ok, fresh.ImportBackup(backup).Status);
        Assert.Single(fresh.Database.MemoEntries);
        Assert.Single(fresh.Database.PathBackupItems); 
    }

    [Fact]
    public void Truncated_V3_Header_Rejected()
    {
        var store = NewStoreWithData("截断样本");
        var backup = Path.Combine(_dir, "trunc.novabak");
        Assert.True(store.ExportBackup(backup, false));

        var all = File.ReadAllBytes(backup);
        File.WriteAllBytes(backup, all[..30]); 

        var fresh = new NovaraStore(Path.Combine(_dir, "t2.novadb"));
        Assert.NotEqual(LoadStatus.Ok, fresh.ImportBackup(backup).Status);
    }

    [Fact]
    public void Wrong_Magic_And_Unknown_Version_Rejected()
    {
        var store = NewStoreWithData("magic样本");
        var backup = Path.Combine(_dir, "magic.novabak");
        Assert.True(store.ExportBackup(backup, false));
        var all = File.ReadAllBytes(backup);

        var badMagic = (byte[])all.Clone();
        badMagic[0] = 0x58; // "X"...
        File.WriteAllBytes(backup, badMagic);
        var fresh = new NovaraStore(Path.Combine(_dir, "m.novadb"));
        Assert.Equal(LoadStatus.Corrupted, fresh.ImportBackup(backup).Status);

        var badVer = (byte[])all.Clone();
        badVer[4] = 4; 
        var p2 = Path.Combine(_dir, "ver.novabak");
        File.WriteAllBytes(p2, badVer);
        Assert.Equal(LoadStatus.Corrupted, new NovaraStore(Path.Combine(_dir, "v.novadb")).ImportBackup(p2).Status);
    }
}
