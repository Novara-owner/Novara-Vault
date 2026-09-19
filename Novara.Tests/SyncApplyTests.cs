using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SyncApplyTests : IDisposable
{
    private const string LockPassword = "lock-pw-123";

    private static readonly string SpaceKey =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "novara-syncapply-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;

    public SyncApplyTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "data.novadb");
    }

    public void Dispose()
    {
        PasswordService.SetBaseDir(null);
        try { Directory.Delete(_dir, true); } catch { }
    }


    private NovaraStore NewLocalStore()
    {
        PasswordService.SetBaseDir(_dir);
        Assert.True(PasswordService.Create(LockPassword));

        var store = new NovaraStore(_dbPath);
        store.Load();
        Assert.True(store.EnableEncryption(LockPassword));

        store.Database.MemoEntries.Add(new MemoEntry { Name = "local-only", Type = "自定义" });
        store.Database.AppSettings.AppLanguage = "zh-CN";
        store.Database.AppSettings.Theme = "浅色模式";
        store.Database.AppSettings.McpToken = "local-mcp-token";
        store.Database.AppSettings.McpEnabled = true;
        store.Database.AppSettings.AutoStart = true;
        store.Database.AppSettings.QuickCaptureHotkey = "AltN";
        store.Database.AppSettings.VisibleTabs = new List<string> { "备忘" };
        Assert.True(store.SaveSync());
        return store;
    }


    private static byte[] RemotePayload(params string[] entryNames)
    {
        var remote = new NovaraDatabase();
        foreach (var name in entryNames)
            remote.MemoEntries.Add(new MemoEntry { Name = name, Type = "自定义" });
        remote.MemoEntries.Add(new MemoEntry { Name = "deleted-on-remote", Type = "自定义", IsDeleted = true, DeletedAt = DateTime.UtcNow });
        remote.AppSettings = SyncFieldPolicy.BuildRoamingSettings(new AppSettings { AppLanguage = "en-US", Theme = "深色模式" });
        return SyncPayloadApplier.BuildPayloadJson(remote);
    }



    [Fact]
    public void SyncPayload_IsABogStandardV4Backup_ImportableByTheReleasedChain()
    {
        PasswordService.SetBaseDir(_dir);
        Assert.True(PasswordService.Create(LockPassword));


        var container = SyncContainer.Seal(RemotePayload("from-remote"), SpaceKey, SyncContainer.NewVersionSalt());
        var file = Path.Combine(_dir, "payload.novaenc");
        File.WriteAllBytes(file, container);


        var target = new NovaraStore(Path.Combine(_dir, "other.novadb"));
        target.Load();
        Assert.True(target.EnableEncryption(LockPassword));

        var result = target.ImportBackup(file, SpaceKey);
        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Equal("from-remote", target.Database.MemoEntries.Single(e => !e.IsDeleted).Name);


        Assert.NotEqual(LoadStatus.Ok, target.ImportBackup(file, SpaceKey + "x").Status);
    }



    [Fact]
    public void ApplyContainer_AdoptsRemoteData_AndKeepsLocalCredentialsAndSwitches()
    {
        var store = NewLocalStore();
        var container = SyncContainer.Seal(RemotePayload("from-remote"), SpaceKey, SyncContainer.NewVersionSalt());

        var outcome = SyncPayloadApplier.ApplyContainer(store, container, SpaceKey);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal("from-remote", store.Database.MemoEntries.Single(e => !e.IsDeleted).Name);
        Assert.DoesNotContain(store.Database.MemoEntries, e => e.Name == "local-only");


        Assert.Equal("local-mcp-token", store.Database.AppSettings.McpToken);
        Assert.True(store.Database.AppSettings.McpEnabled);
        Assert.True(store.Database.AppSettings.AutoStart);
        Assert.Equal("AltN", store.Database.AppSettings.QuickCaptureHotkey);
        Assert.Equal(new List<string> { "备忘" }, store.Database.AppSettings.VisibleTabs);
        Assert.True(store.Database.AppSettings.PrivacyLockEnabled);


        Assert.Equal("en-US", store.Database.AppSettings.AppLanguage);
        Assert.Equal("深色模式", store.Database.AppSettings.Theme);


        Assert.Contains(store.Database.MemoEntries, e => e.IsDeleted);


        var reload = new NovaraStore(_dbPath);
        Assert.Equal(LoadStatus.Ok, reload.LoadWithPassword(LockPassword).Status);
        Assert.Equal("from-remote", reload.Database.MemoEntries.Single(e => !e.IsDeleted).Name);
        Assert.Equal("local-mcp-token", reload.Database.AppSettings.McpToken);
        Assert.Equal("en-US", reload.Database.AppSettings.AppLanguage);
    }

    [Fact]
    public void ApplyEnvelope_AdoptsAVerifiedEnvelope()
    {
        var store = NewLocalStore();
        var container = SyncContainer.Seal(RemotePayload("via-envelope"), SpaceKey, SyncContainer.NewVersionSalt());
        var envelope = new SyncEnvelope
        {
            Space = "sp1",
            Device = "dev1",
            Base = 0,
            Version = 1,
            Ts = SyncEnvelopeCodec.UtcStamp(DateTimeOffset.UtcNow),
            Payload = Convert.ToBase64String(container),
        };
        SyncEnvelopeCodec.Seal(envelope, SpaceKey);

        var outcome = SyncPayloadApplier.ApplyEnvelope(store, SyncEnvelopeCodec.Serialize(envelope), SpaceKey);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal("via-envelope", store.Database.MemoEntries.Single(e => !e.IsDeleted).Name);
    }

    [Fact]
    public void ApplyEnvelope_RejectsATamperedEnvelope_WithoutTouchingTheDatabase()
    {
        var store = NewLocalStore();
        var container = SyncContainer.Seal(RemotePayload("payload"), SpaceKey, SyncContainer.NewVersionSalt());
        var envelope = new SyncEnvelope
        {
            Space = "sp1",
            Device = "dev1",
            Base = 0,
            Version = 1,
            Ts = SyncEnvelopeCodec.UtcStamp(DateTimeOffset.UtcNow),
            Payload = Convert.ToBase64String(container),
        };
        SyncEnvelopeCodec.Seal(envelope, SpaceKey);


        var json = SyncEnvelopeCodec.Serialize(envelope).Replace("\"base\":0", "\"base\":8");
        var outcome = SyncPayloadApplier.ApplyEnvelope(store, json, SpaceKey);

        Assert.False(outcome.Success);
        Assert.Equal(LoadStatus.Corrupted, outcome.Status);
        Assert.Equal("local-only", store.Database.MemoEntries.Single(e => !e.IsDeleted).Name);
    }

    [Fact]
    public void ApplyEnvelope_RejectsTheWrongSpaceKey()
    {
        var store = NewLocalStore();
        var container = SyncContainer.Seal(RemotePayload("payload"), SpaceKey, SyncContainer.NewVersionSalt());
        var envelope = new SyncEnvelope
        {
            Space = "sp1",
            Device = "dev1",
            Base = 0,
            Version = 1,
            Ts = SyncEnvelopeCodec.UtcStamp(DateTimeOffset.UtcNow),
            Payload = Convert.ToBase64String(container),
        };
        SyncEnvelopeCodec.Seal(envelope, SpaceKey);

        var wrongKey = Convert.ToBase64String(Enumerable.Range(0x40, 32).Select(i => (byte)i).ToArray());
        var outcome = SyncPayloadApplier.ApplyEnvelope(store, SyncEnvelopeCodec.Serialize(envelope), wrongKey);

        Assert.False(outcome.Success);
        Assert.Equal("local-only", store.Database.MemoEntries.Single(e => !e.IsDeleted).Name);
    }

    [Fact]
    public void ApplyContainer_RejectsATamperedContainer()
    {
        var store = NewLocalStore();
        var container = SyncContainer.Seal(RemotePayload("payload"), SpaceKey, SyncContainer.NewVersionSalt());
        container[^1] ^= 0xFF;

        var outcome = SyncPayloadApplier.ApplyContainer(store, container, SpaceKey);

        Assert.False(outcome.Success);
        Assert.Equal(LoadStatus.Corrupted, outcome.Status);
        Assert.Equal("local-only", store.Database.MemoEntries.Single(e => !e.IsDeleted).Name);
    }

    [Fact]
    public void ApplyContainer_RefusesALockedStore_APlaintextStore_AndAPendingRestore()
    {
        var encrypted = NewLocalStore();
        var container = SyncContainer.Seal(RemotePayload("payload"), SpaceKey, SyncContainer.NewVersionSalt());



        var locked = new NovaraStore(Path.Combine(_dir, "locked.novadb"));
        Assert.Equal(LoadStatus.NeedPassword, SyncPayloadApplier.ApplyContainer(locked, container, SpaceKey).Status);


        var plain = new NovaraStore(Path.Combine(_dir, "plain.novadb"));
        plain.Load();
        Assert.Equal(LoadStatus.IoError, SyncPayloadApplier.ApplyContainer(plain, container, SpaceKey).Status);


        encrypted.SetSuppressSave(true);
        var pending = SyncPayloadApplier.ApplyContainer(encrypted, container, SpaceKey);
        Assert.False(pending.Success);
        Assert.Equal(LoadStatus.IoError, pending.Status);
    }

    [Fact]
    public void ApplyContainer_RejectsAMalformedSpaceKey()
    {
        var store = NewLocalStore();
        var container = SyncContainer.Seal(RemotePayload("payload"), SpaceKey, SyncContainer.NewVersionSalt());

        Assert.Equal(LoadStatus.Corrupted, SyncPayloadApplier.ApplyContainer(store, container, "not-a-key").Status);
        Assert.Equal(LoadStatus.Corrupted, SyncPayloadApplier.ApplyEnvelope(store, "{}", SpaceKey).Status);
    }

    [Fact]
    public void BuildPayloadJson_StripsCredentials_And_KeepsTombstones()
    {
        var store = NewLocalStore();
        store.Database.MemoEntries.Add(new MemoEntry { Name = "gone", Type = "自定义", IsDeleted = true, DeletedAt = DateTime.UtcNow });

        var payload = SyncPayloadApplier.BuildPayloadJson(store.Database);
        var parsed = System.Text.Json.JsonSerializer.Deserialize<NovaraDatabase>(payload,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })!;

        Assert.Equal("", parsed.AppSettings.McpToken);
        Assert.False(parsed.AppSettings.McpEnabled);
        Assert.False(parsed.AppSettings.AutoStart);
        Assert.Contains(parsed.MemoEntries, e => e.IsDeleted);
        Assert.Contains(parsed.MemoEntries, e => e.Name == "gone" && e.IsDeleted);
    }
}
