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
        try { Directory.Delete(_dir, recursive: true); } catch { } // N5V-04: clear Hidden|ReadOnly first or the delete throws and leaks the temp dir
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
    public void ChangePassword_OldFailsNewWorks()
    {
        PasswordService.Create("old-pw");
        Assert.True(PasswordService.ChangePassword("old-pw", "new-pw"));
        Assert.False(PasswordService.Verify("old-pw"));
        Assert.True(PasswordService.Verify("new-pw"));
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
        try { Directory.Delete(_dir, recursive: true); } catch { } // N5V-04: clear Hidden|ReadOnly first or the delete throws and leaks the temp dir
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
