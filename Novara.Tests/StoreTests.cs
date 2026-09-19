using System.Text.Json;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;



[CollectionDefinition("CoreSequential", DisableParallelization = true)]
public class CoreSequentialCollection { }

[Collection("CoreSequential")]
public class PasswordServiceTests : IDisposable
{
    private readonly string _dir;

    public PasswordServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "novara-test-" + Guid.NewGuid().ToString("N"));
        PasswordService.SetBaseDir(_dir);
    }

    public void Dispose()
    {
        try { foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); } catch { }
        try { Directory.Delete(_dir, recursive: true); } catch { }
        PasswordService.SetBaseDir(null);
    }

    [Fact]
    public void Create_ThenVerify_ReturnsTrue()
    {
        Assert.True(PasswordService.Create("secret-password"));
        Assert.True(PasswordService.Verify("secret-password"));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        PasswordService.Create("correct");
        Assert.False(PasswordService.Verify("wrong"));
    }

    [Fact]
    public void StagedRekey_CrashBeforePromote_NewPasswordRecoversAtStartup()
    {



        PasswordService.Create("old-pw");
        var store = new NovaraStore(Path.Combine(_dir, "rekey1.novadb"));
        store.Load();
        Assert.True(store.EnableEncryption("old-pw"));
        Assert.True(PasswordService.StageChange("new-pw"));
        Assert.True(store.Reencrypt("new-pw"));



        var restarted = new NovaraStore(Path.Combine(_dir, "rekey1.novadb"));
        Assert.Equal(LoadStatus.Ok, restarted.LoadWithPassword("new-pw").Status);
        Assert.True(restarted.IsEncrypted);
        Assert.False(PasswordService.HasStagedChange());
        Assert.True(PasswordService.Verify("new-pw"));
        Assert.False(PasswordService.Verify("old-pw"));
    }

    [Fact]
    public void StagedRekey_TypedOldPassword_NeverReportsCorruption()
    {



        PasswordService.Create("old-pw");
        var store = new NovaraStore(Path.Combine(_dir, "rekey2.novadb"));
        store.Load();
        Assert.True(store.EnableEncryption("old-pw"));
        PasswordService.StageChange("new-pw");
        Assert.True(store.Reencrypt("new-pw"));

        var restarted = new NovaraStore(Path.Combine(_dir, "rekey2.novadb"));
        Assert.Equal(LoadStatus.WrongPassword, restarted.LoadWithPassword("old-pw").Status);
        Assert.True(PasswordService.HasStagedChange());

        var retry = new NovaraStore(Path.Combine(_dir, "rekey2.novadb"));
        Assert.Equal(LoadStatus.Ok, retry.LoadWithPassword("new-pw").Status);
        Assert.False(PasswordService.HasStagedChange());
    }

    [Fact]
    public void StagedRekey_CrashBeforeReencrypt_TypedNewPassword_MustNotDestroyOldHash()
    {






        PasswordService.Create("old-pw");
        var store = new NovaraStore(Path.Combine(_dir, "rekey3.novadb"));
        store.Load();
        Assert.True(store.EnableEncryption("old-pw"));
        Assert.True(PasswordService.StageChange("new-pw"));

        var restarted = new NovaraStore(Path.Combine(_dir, "rekey3.novadb"));
        Assert.Equal(LoadStatus.WrongPassword, restarted.LoadWithPassword("new-pw").Status);
        Assert.True(PasswordService.HasStagedChange());
        Assert.True(PasswordService.Verify("old-pw"));
        Assert.False(PasswordService.Verify("new-pw"));


        var retry = new NovaraStore(Path.Combine(_dir, "rekey3.novadb"));
        Assert.Equal(LoadStatus.Ok, retry.LoadWithPassword("old-pw").Status);
        Assert.False(PasswordService.HasStagedChange());
    }

    [Fact]
    public void Create_ReplacesSecurityFile_AndDiscardsAnyStagedRecord()
    {



        Assert.True(PasswordService.Create("first"));
        Assert.True(PasswordService.StageChange("second"));
        Assert.True(PasswordService.HasStagedChange());

        Assert.True(PasswordService.Create("third"));
        Assert.False(PasswordService.HasStagedChange());
        Assert.True(PasswordService.Verify("third"));
    }

    [Fact]
    public void DiscardOrphanedPasswordFiles_WipesSecurityWithoutDatabase_KeepsItWhenDbExists()
    {






        var dbPath = Path.Combine(_dir, "data.novadb");
        Assert.True(PasswordService.Create("pw"));
        PasswordService.SetLockoutEnabled(true);
        NovaraStore.DiscardOrphanedPasswordFiles(dbPath);
        Assert.False(PasswordService.Exists());
        Assert.False(File.Exists(Path.Combine(_dir, "lockout.dat")));


        Assert.True(PasswordService.Create("pw2"));
        File.WriteAllBytes(dbPath, new byte[] { 1, 2, 3 });
        NovaraStore.DiscardOrphanedPasswordFiles(dbPath);
        Assert.True(PasswordService.Exists());
    }

    [Fact]
    public void GetDeriveSalt_AfterCreate_Returns32Bytes()
    {
        PasswordService.Create("pw");
        var salt = PasswordService.GetDeriveSalt();
        Assert.NotNull(salt);
        Assert.Equal(32, salt!.Length);
    }

    [Fact]
    public void Lockout_IsLockedOut_ReflectsUntil()
    {
        Assert.False(PasswordService.IsLockedOut(out _));
        PasswordService.SetLockoutUntil(DateTime.Now.AddMinutes(5));
        Assert.True(PasswordService.IsLockedOut(out var remaining));
        Assert.True(remaining > TimeSpan.Zero);
        PasswordService.ClearLockout();
        Assert.False(PasswordService.IsLockedOut(out _));
    }

    [Fact]
    public void Lockout_MonotonicClock_RemainingIsConsistent()
    {


        PasswordService.SetLockoutUntil(DateTime.Now.AddMinutes(5));
        var remaining = PasswordService.GetRemainingLockout();
        Assert.True(remaining > TimeSpan.FromMinutes(4));
        Assert.True(remaining <= TimeSpan.FromMinutes(5));
        PasswordService.ClearLockout();
        Assert.Equal(TimeSpan.Zero, PasswordService.GetRemainingLockout());
    }

    [Fact]
    public void FailCount_RoundTrip()
    {
        PasswordService.SetFailCount(3);
        Assert.Equal(3, PasswordService.GetFailCount());
        PasswordService.SetFailCount(0);
        Assert.Equal(0, PasswordService.GetFailCount());
    }
}

[Collection("CoreSequential")]
public class NovaraStoreTests : IDisposable
{
    private readonly string _dir;

    public NovaraStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "novara-store-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        PasswordService.SetBaseDir(_dir);
    }

    public void Dispose()
    {
        try { foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); } catch { }
        try { Directory.Delete(_dir, recursive: true); } catch { }
        PasswordService.SetBaseDir(null);
    }

    private string DbPath => Path.Combine(_dir, "data.novadb");

    [Fact]
    public void Plaintext_RoundTrip_ReturnsData()
    {
        var path = DbPath;
        var store = new NovaraStore(path);
        Assert.Equal(LoadStatus.EmptyCreated, store.Load().Status);

        store.Database.MemoEntries.Add(new MemoEntry { Name = "测试条目", Type = "自定义" });
        store.Database.TodoCards.Add(new TodoCard { Title = "测试待办" });
        Assert.True(store.SaveSync());

        var reload = new NovaraStore(path);
        Assert.Equal(LoadStatus.Ok, reload.Load().Status);
        Assert.Single(reload.Database.MemoEntries);
        Assert.Single(reload.Database.TodoCards);
        Assert.Equal("测试条目", reload.Database.MemoEntries[0].Name);
    }

    [Fact]
    public void Load_CorruptMagic_ReturnsCorrupted()
    {
        var path = DbPath;

        var bad = new byte[] { 0x11, 0x22, 0x33, 0x44, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        File.WriteAllBytes(path, bad);

        var store = new NovaraStore(path);
        Assert.Equal(LoadStatus.Corrupted, store.Load().Status);
    }

    [Fact]
    public void Load_NullElementsInsideLists_NormalizedNotCorrupted()
    {



        var path = DbPath;
        var db = new NovaraDatabase();
        db.MemoEntries.Add(null!);
        db.MemoEntries.Add(new MemoEntry { Name = "正常", Type = "账户", Fields = new List<EntryField> { null!, new() { Label = null!, Value = null! } } });
        db.TodoCards.Add(null!);
        db.TodoCards.Add(new TodoCard { Title = "待办", SubTexts = new List<string> { null!, "子项" } });
        var json = JsonSerializer.SerializeToUtf8Bytes(db, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using (var fs = File.Create(path))
        {
            var header = new byte[22];
            BitConverter.TryWriteBytes(header.AsSpan(0, 4), 0x41564F4E);
            header[4] = 1; header[5] = 0;
            System.Security.Cryptography.MD5.HashData(json).CopyTo(header, 6);
            fs.Write(header); fs.Write(json);
        }

        var store = new NovaraStore(path);
        Assert.Equal(LoadStatus.Ok, store.Load().Status);
        var loaded = store.Database;
        Assert.Single(loaded.MemoEntries);
        Assert.Single(loaded.MemoEntries[0].Fields);
        Assert.Equal("", loaded.MemoEntries[0].Fields[0].Label);
        Assert.Single(loaded.TodoCards);
        Assert.Equal(new List<string> { "子项" }, loaded.TodoCards[0].SubTexts);
    }




    private void WriteRawDb(NovaraDatabase db)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(db, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var fs = File.Create(DbPath);
        var header = new byte[22];
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), 0x41564F4E);
        header[4] = 1; header[5] = 0;
        System.Security.Cryptography.MD5.HashData(json).CopyTo(header, 6);
        fs.Write(header); fs.Write(json);
    }

    [Fact]
    public void Load_NullSubText_DropsTheMatchingCheckedState_NotJustTheText()
    {



        var db = new NovaraDatabase();
        db.TodoCards.Add(new TodoCard
        {
            Title = "待办",
            SubTexts = new List<string> { null!, "B" },
            CheckedStates = new List<bool> { false, false, true },
        });
        WriteRawDb(db);

        var store = new NovaraStore(DbPath);
        Assert.Equal(LoadStatus.Ok, store.Load().Status);
        var td = store.Database.TodoCards[0];
        Assert.Equal(new List<string> { "B" }, td.SubTexts);
        Assert.Equal(new List<bool> { false, true }, td.CheckedStates);
    }

    [Fact]
    public void Load_CheckedStatesLongerThanRows_IsTruncatedToTheRows()
    {


        var db = new NovaraDatabase();
        db.TodoCards.Add(new TodoCard
        {
            Title = "待办",
            SubTexts = new List<string> { "A" },
            CheckedStates = new List<bool> { true, true, true, true },
        });
        WriteRawDb(db);

        var store = new NovaraStore(DbPath);
        Assert.Equal(LoadStatus.Ok, store.Load().Status);
        Assert.Equal(new List<bool> { true, true }, store.Database.TodoCards[0].CheckedStates);
    }

    [Fact]
    public void Load_CheckedStatesShorterThanRows_IsPaddedUnchecked()
    {
        var db = new NovaraDatabase();
        db.TodoCards.Add(new TodoCard
        {
            Title = "待办",
            SubTexts = new List<string> { "A", "B" },
            CheckedStates = new List<bool> { true },
        });
        WriteRawDb(db);

        var store = new NovaraStore(DbPath);
        Assert.Equal(LoadStatus.Ok, store.Load().Status);
        Assert.Equal(new List<bool> { true, false, false }, store.Database.TodoCards[0].CheckedStates);
    }

    [Fact]
    public void Encrypted_RoundTrip_ReturnsData()
    {
        var path = DbPath;

        var store = new NovaraStore(path);
        Assert.Equal(LoadStatus.EmptyCreated, store.Load().Status);
        store.Database.MemoEntries.Add(new MemoEntry { Name = "加密条目", Type = "自定义" });
        Assert.True(store.SaveSync());


        PasswordService.Create("encrypt-pw");
        Assert.True(store.EnableEncryption("encrypt-pw"));


        var reload = new NovaraStore(path);
        Assert.Equal(LoadStatus.Encrypted, reload.Load().Status);
        Assert.Equal(LoadStatus.Ok, reload.LoadWithPassword("encrypt-pw").Status);
        Assert.Single(reload.Database.MemoEntries);
        Assert.Equal("加密条目", reload.Database.MemoEntries[0].Name);
    }

    [Fact]
    public void LoadWithPassword_WrongPassword_ReturnsWrongPassword()
    {
        var path = DbPath;
        var store = new NovaraStore(path);
        store.Load();
        PasswordService.Create("right-pw");
        Assert.True(store.EnableEncryption("right-pw"));

        var reload = new NovaraStore(path);
        Assert.Equal(LoadStatus.Encrypted, reload.Load().Status);
        Assert.Equal(LoadStatus.WrongPassword, reload.LoadWithPassword("wrong-pw").Status);
    }


    [Fact]
    public void McpToken_SurvivesEncryptionRoundTrip()
    {
        var path = DbPath;
        var store = new NovaraStore(path);
        store.Load();
        store.Database.AppSettings.McpToken = "mcp-token-123";
        Assert.True(store.SaveSync());

        PasswordService.Create("encrypt-pw");
        Assert.True(store.EnableEncryption("encrypt-pw"));

        var reload = new NovaraStore(path);
        Assert.Equal(LoadStatus.Encrypted, reload.Load().Status);
        Assert.Equal(LoadStatus.Ok, reload.LoadWithPassword("encrypt-pw").Status);
        Assert.Equal("mcp-token-123", reload.Database.AppSettings.McpToken);
    }



    [Fact]
    public void WriteSnapshot_PlaintextHeaderCarriesMd5_ButGcmHeaderLeavesItZero()
    {




        var path = DbPath;
        var store = new NovaraStore(path);
        Assert.Equal(LoadStatus.EmptyCreated, store.Load().Status);
        store.Database.MemoEntries.Add(new MemoEntry { Name = "有内容", Type = "自定义" });
        Assert.True(store.SaveSync());

        var plaintext = File.ReadAllBytes(path);
        Assert.Equal(0x41564F4Eu, BitConverter.ToUInt32(plaintext, 0));
        Assert.Equal(1, plaintext[4]);
        Assert.Contains(plaintext.Skip(6).Take(16), b => b != 0);

        PasswordService.Create("md5-pw");
        Assert.True(store.EnableEncryption("md5-pw"));
        Assert.True(store.SaveSync());

        var encrypted = File.ReadAllBytes(path);
        Assert.Equal(0x41564F4Eu, BitConverter.ToUInt32(encrypted, 0));
        Assert.Equal(3, encrypted[4]);
        Assert.All(encrypted.Skip(6).Take(16), b => Assert.Equal(0, b));


        var reload = new NovaraStore(path);
        Assert.Equal(LoadStatus.Ok, reload.LoadWithPassword("md5-pw").Status);
        Assert.Equal("有内容", reload.Database.MemoEntries.Single().Name);
    }



    [Fact]
    public void ExportSnapshot_ResetsTheMcpSwitchesAlongWithTheCredentials()
    {





        var path = DbPath;
        var store = new NovaraStore(path);
        Assert.Equal(LoadStatus.EmptyCreated, store.Load().Status);
        var s = store.Database.AppSettings;
        s.McpEnabled = true;
        s.McpDeleteEnabled = true;
        s.McpPermMigrated = true;
        s.McpDetailExpanded = false;
        s.McpToken = "token-must-not-travel";
        s.McpAllowedProcesses.Add("/opt/agent");
        s.McpClientPermissions.Add(new McpClientPermRecord { Path = "/opt/agent", Permissions = 1 });
        Assert.True(store.SaveSync());

        var cipher = store.ExportSnapshotCipher("snapshot-pw", true, true, true, true);
        Assert.NotNull(cipher);
        var snapshotFile = Path.Combine(_dir, "snapshot.bin");
        File.WriteAllBytes(snapshotFile, Convert.FromBase64String(cipher!));

        var receiver = new NovaraStore(Path.Combine(_dir, "receiver.novadb"));
        receiver.Load();
        Assert.Equal(LoadStatus.Ok, receiver.ImportBackup(snapshotFile, "snapshot-pw").Status);

        var got = receiver.Database.AppSettings;
        Assert.Equal("", got.McpToken);
        Assert.Empty(got.McpAllowedProcesses);
        Assert.Empty(got.McpClientPermissions);
        Assert.False(got.McpEnabled);
        Assert.False(got.McpDeleteEnabled);
        Assert.False(got.McpPermMigrated);
        Assert.True(got.McpDetailExpanded);
    }



    [Fact]
    public void SaveSync_WhileSuppressed_ReportsSuccessButWritesNothing()
    {





        var path = DbPath;
        var store = new NovaraStore(path);
        Assert.Equal(LoadStatus.EmptyCreated, store.Load().Status);
        store.Database.MemoEntries.Add(new MemoEntry { Name = "落盘过", Type = "自定义" });
        Assert.True(store.SaveSync());
        var onDisk = File.ReadAllBytes(path);

        store.Database.MemoEntries.Add(new MemoEntry { Name = "不该落盘", Type = "自定义" });
        store.SetSuppressSave(true);
        Assert.True(store.SaveSync());
        Assert.Equal(onDisk, File.ReadAllBytes(path));
    }

    [Fact]
    public void ImportBackup_WhileSuppressed_RefusesInsteadOfReportingAFakeSuccess()
    {




        var path = DbPath;
        var store = new NovaraStore(path);
        Assert.Equal(LoadStatus.EmptyCreated, store.Load().Status);
        store.Database.MemoEntries.Add(new MemoEntry { Name = "本机", Type = "自定义" });
        Assert.True(store.SaveSync());

        var backup = Path.Combine(_dir, "b.novabak");
        Assert.True(store.ExportBackup(backup, false));

        store.SetSuppressSave(true);
        var before = store.Database;
        var result = store.ImportBackup(backup);

        Assert.Equal(LoadStatus.IoError, result.Status);

        Assert.Equal(NovaraStore.RestorePendingMessage, result.Detail);
        Assert.Same(before, store.Database);
    }
}


public class DiaryEntrySerializationTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void LegacyJson_WithoutFormat_DefaultsToHtml()
    {

        const string legacy = "{\"id\":\"abc\",\"title\":\"旧日记\",\"content\":\"<p>hi</p>\"}";
        var entry = JsonSerializer.Deserialize<DiaryEntry>(legacy, Opts);
        Assert.NotNull(entry);
        Assert.Equal("html", entry!.Format);
    }

    [Fact]
    public void MarkdownJson_WithFormat_DeserializesMarkdown()
    {
        const string json = "{\"id\":\"abc\",\"title\":\"文档\",\"format\":\"markdown\"}";
        var entry = JsonSerializer.Deserialize<DiaryEntry>(json, Opts);
        Assert.NotNull(entry);
        Assert.Equal("markdown", entry!.Format);
    }

    [Fact]
    public void NewEntry_DefaultFormat_IsHtml()
    {
        Assert.Equal("html", new DiaryEntry().Format);
    }
}
