using System.Security.Cryptography;
using System.Text.Json;
using Novara.Services;
using Xunit;

namespace Novara.Tests;


[Collection("CoreSequential")]
public class DatabaseHealthTests : IDisposable
{
    private readonly string _dir;

    public DatabaseHealthTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "novara-health-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static Novara.Models.NovaraDatabase NewDb(int groups = 1)
    {
        var db = new Novara.Models.NovaraDatabase();
        for (int i = 0; i < groups; i++)
            db.MemoGroups.Add(new Novara.Models.MemoGroup { Name = "组" + i });
        return db;
    }

    

    [Fact]
    public void Orphans_None_ReturnsZero()
    {
        var db = NewDb();
        db.MemoEntries.Add(new Novara.Models.MemoEntry { Name = "正常", GroupId = db.MemoGroups[0].Id });
        db.MemoEntries.Add(new Novara.Models.MemoEntry { Name = "独立", GroupId = null });
        Assert.Equal(0, DatabaseHealth.CountOrphanMemoEntries(db));
    }

    [Fact]
    public void Orphans_HangingReferences_Counted_IncludingSoftDeleted()
    {
        var db = NewDb();
        db.MemoEntries.Add(new Novara.Models.MemoEntry { Name = "悬挂", GroupId = Guid.NewGuid() });
        db.MemoEntries.Add(new Novara.Models.MemoEntry { Name = "软删悬挂", GroupId = Guid.NewGuid(), IsDeleted = true });
        db.MemoEntries.Add(new Novara.Models.MemoEntry { Name = "正常", GroupId = db.MemoGroups[0].Id });
        Assert.Equal(2, DatabaseHealth.CountOrphanMemoEntries(db)); 
    }

    [Fact]
    public void Orphans_NullDb_ReturnsZero()
    {
        Assert.Equal(0, DatabaseHealth.CountOrphanMemoEntries(null!));
    }

    

    private string WriteDataFile(byte[] header, byte[] body)
    {
        var path = Path.Combine(_dir, "d-" + Guid.NewGuid().ToString("N") + ".novadb");
        using (var fs = File.Create(path)) { fs.Write(header); fs.Write(body); }
        return path;
    }

    private static byte[] PlainV1Header(byte[] body)
    {
        var header = new byte[22];
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), 0x41564F4E);
        header[4] = 1; header[5] = 0;
        MD5.HashData(body).CopyTo(header, 6);
        return header;
    }

    [Fact]
    public void Verify_PlaintextV1_CorrectFile_DigestVerified()
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(NewDb(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.Equal(DataFileIntegrity.DigestVerified, DatabaseHealth.VerifyDataFile(WriteDataFile(PlainV1Header(body), body)));
    }

    [Fact]
    public void Verify_PlaintextV1_TamperedBody_Failed()
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(NewDb(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var header = PlainV1Header(body); 
        body[^1] ^= 0xFF;                 
        Assert.Equal(DataFileIntegrity.Failed, DatabaseHealth.VerifyDataFile(WriteDataFile(header, body)));
    }

    [Fact]
    public void Verify_BadMagic_TooShort_Or_Missing_Failed()
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(NewDb(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var header = PlainV1Header(body);
        header[0] = 0x58; // "X"
        Assert.Equal(DataFileIntegrity.Failed, DatabaseHealth.VerifyDataFile(WriteDataFile(header, body)));
        Assert.Equal(DataFileIntegrity.Failed, DatabaseHealth.VerifyDataFile(WriteDataFile(header[..10], []))); // < 22B
        Assert.Equal(DataFileIntegrity.Failed, DatabaseHealth.VerifyDataFile(Path.Combine(_dir, "missing.novadb")));
    }

    [Fact]
    public void Verify_GcmV2_Structured_PlaintextV2Matrix_Rejected()
    {
        var gcmBody = new byte[12 + 16 + 32]; 
        RandomNumberGenerator.Fill(gcmBody);
        var h2 = new byte[22];
        BitConverter.TryWriteBytes(h2.AsSpan(0, 4), 0x41564F4E);
        h2[4] = 2; h2[5] = 1; 
        Assert.Equal(DataFileIntegrity.EncryptedStructured, DatabaseHealth.VerifyDataFile(WriteDataFile(h2, gcmBody)));

        var truncated = gcmBody[..20]; 
        Assert.Equal(DataFileIntegrity.Failed, DatabaseHealth.VerifyDataFile(WriteDataFile(h2, truncated)));

        var cbfBody = new byte[16 + 32]; 
        RandomNumberGenerator.Fill(cbfBody);
        var h1c = new byte[22];
        BitConverter.TryWriteBytes(h1c.AsSpan(0, 4), 0x41564F4E);
        h1c[4] = 1; h1c[5] = 1; 
        Assert.Equal(DataFileIntegrity.EncryptedStructured, DatabaseHealth.VerifyDataFile(WriteDataFile(h1c, cbfBody)));

        var h2p = new byte[22];
        BitConverter.TryWriteBytes(h2p.AsSpan(0, 4), 0x41564F4E);
        h2p[4] = 2; h2p[5] = 0; 
        Assert.Equal(DataFileIntegrity.Failed, DatabaseHealth.VerifyDataFile(WriteDataFile(h2p, gcmBody)));
    }

    [Fact]
    public void Verify_GcmV3_KdfHardened_Structured_And_ExplicitVersionGate()
    {
        
        
        var gcmBody = new byte[12 + 16 + 32];
        RandomNumberGenerator.Fill(gcmBody);
        var h3 = new byte[22];
        BitConverter.TryWriteBytes(h3.AsSpan(0, 4), 0x41564F4E);
        h3[4] = 3; h3[5] = 1; // v3 + encrypted
        Assert.Equal(DataFileIntegrity.EncryptedStructured, DatabaseHealth.VerifyDataFile(WriteDataFile(h3, gcmBody)));

        var truncated = gcmBody[..20]; 
        Assert.Equal(DataFileIntegrity.Failed, DatabaseHealth.VerifyDataFile(WriteDataFile(h3, truncated)));

        var h3p = new byte[22];
        BitConverter.TryWriteBytes(h3p.AsSpan(0, 4), 0x41564F4E);
        h3p[4] = 3; h3p[5] = 0; 
        Assert.Equal(DataFileIntegrity.Failed, DatabaseHealth.VerifyDataFile(WriteDataFile(h3p, gcmBody)));
    }
}
