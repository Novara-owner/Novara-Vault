using System.Security.Cryptography;
using System.Text.Json;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;



[Collection("CoreSequential")]
public class KdfHardeningTests : IDisposable
{
    private readonly string _dir;

    public KdfHardeningTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "novara-kdf-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private NovaraStore NewStoreWithData()
    {
        var store = new NovaraStore(Path.Combine(_dir, "data-" + Guid.NewGuid().ToString("N") + ".novadb"));
        store.Load();
        store.Database.MemoEntries.Add(new Novara.Models.MemoEntry { Name = "kdf条目", Type = "自定义" });
        return store;
    }

    private static byte VersionByte(string path)
    {
        using var fs = File.OpenRead(path);
        var b = new byte[6];
        fs.ReadExactly(b);
        return b[4];
    }

    

    [Fact]
    public void NewEncryption_StartsAtVer3_And_RoundTrips()
    {
        PasswordService.SetBaseDir(_dir);
        try
        {
            Assert.True(PasswordService.Create("pw-123"));
            var dbPath = Path.Combine(_dir, "data-" + Guid.NewGuid().ToString("N") + ".novadb");
            var store = new NovaraStore(dbPath);
            store.Load();
            store.Database.MemoEntries.Add(new Novara.Models.MemoEntry { Name = "kdf条目", Type = "自定义" });
            Assert.True(store.EnableEncryption("pw-123"));
            Assert.Equal(3, VersionByte(dbPath)); // day-one ver3 (9.2#7)

            var reload = new NovaraStore(dbPath);
            Assert.Equal(LoadStatus.Encrypted, reload.Load().Status);
            Assert.Equal(LoadStatus.Ok, reload.LoadWithPassword("pw-123").Status);
            Assert.Single(reload.Database.MemoEntries);
            Assert.False(reload.NeedsKdfMigration); // ver3 is the target state
        }
        finally { PasswordService.SetBaseDir(null); }
    }

    

    [Fact]
    public void V2_LegacyLibrary_NeedsKdfMigration_And_MigratesToVer3()
    {
        
        
        PasswordService.SetBaseDir(_dir);
        try
        {
            Assert.True(PasswordService.Create("old-pw"));
            var deriveSalt = PasswordService.GetDeriveSalt()!;
            var dbPath = Path.Combine(_dir, "v2-" + Guid.NewGuid().ToString("N") + ".novadb");
            var db = new NovaraDatabase();
            db.MemoEntries.Add(new Novara.Models.MemoEntry { Name = "旧库条目", Type = "自定义" });
            var json = JsonSerializer.SerializeToUtf8Bytes(db, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var cipher = CryptoService.EncryptGcm(json, "old-pw", deriveSalt); 
            using (var fs = File.Create(dbPath))
            {
                var header = new byte[22];
                BitConverter.TryWriteBytes(header.AsSpan(0, 4), 0x41564F4E);
                header[4] = 2; header[5] = 1; // v2 + GCM
                fs.Write(header); fs.Write(cipher);
            }

            var store = new NovaraStore(dbPath);
            Assert.Equal(LoadStatus.Encrypted, store.Load().Status);
            Assert.Equal(LoadStatus.Ok, store.LoadWithPassword("old-pw").Status);
            Assert.True(store.NeedsKdfMigration); 

            Assert.True(store.MigrateKdf());
            Assert.Equal(3, VersionByte(dbPath)); 
            Assert.False(store.NeedsKdfMigration);
            Assert.False(store.MigrateKdf()); 

            
            var reload = new NovaraStore(dbPath);
            Assert.Equal(LoadStatus.Ok, reload.LoadWithPassword("old-pw").Status);
            Assert.Single(reload.Database.MemoEntries);
        }
        finally { PasswordService.SetBaseDir(null); }
    }

    [Fact]
    public void PlaintextLibrary_NeverNeedsKdfMigration()
    {
        var store = NewStoreWithData();
        Assert.False(store.NeedsKdfMigration);
        Assert.False(store.MigrateKdf());
    }

    

    [Fact]
    public void SecurityFile_VerifyLegacyFormat_Then_AutoUpgrade()
    {
        
        PasswordService.SetBaseDir(_dir);
        try
        {
            var hashSalt = RandomNumberGenerator.GetBytes(32);
            var deriveSalt = RandomNumberGenerator.GetBytes(32);
            byte[] LegacyHash(string pw, byte[] salt)
            {
                using var sha = SHA256.Create();
                var input = new byte[salt.Length + System.Text.Encoding.UTF8.GetByteCount(pw)];
                salt.CopyTo(input, 0);
                System.Text.Encoding.UTF8.GetBytes(pw, input.AsSpan(salt.Length));
                return sha.ComputeHash(input);
            }
            var legacyJson = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["HashSalt"] = Convert.ToBase64String(hashSalt),
                ["DeriveSalt"] = Convert.ToBase64String(deriveSalt),
                ["Hash"] = Convert.ToBase64String(LegacyHash("legacy-pw", hashSalt)),
                
            });
            File.WriteAllText(Path.Combine(_dir, "security.dat"), legacyJson);

            
            Assert.True(PasswordService.Verify("legacy-pw"));
            var upgraded = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(_dir, "security.dat")));
            Assert.Equal(2, upgraded.GetProperty("Version").GetInt32());
            Assert.False(upgraded.TryGetProperty("Hash", out var h) && h.GetString() == Convert.ToBase64String(LegacyHash("legacy-pw", hashSalt))); 

            
            Assert.True(PasswordService.Verify("legacy-pw"));
            Assert.False(PasswordService.Verify("wrong"));
        }
        finally { PasswordService.SetBaseDir(null); }
    }
}
