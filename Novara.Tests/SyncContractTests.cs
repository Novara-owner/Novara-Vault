using System.Security.Cryptography;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SyncContractTests
{

    private static string FixedKey(byte seed = 0)
    {
        var key = new byte[SyncKeyWrap.SpaceKeySize];
        for (int i = 0; i < key.Length; i++) key[i] = (byte)(seed + i);
        return Convert.ToBase64String(key);
    }

    private static byte[] FixedSalt(byte seed = 0x20)
    {
        var salt = new byte[SyncKeyWrap.SaltSize];
        for (int i = 0; i < salt.Length; i++) salt[i] = (byte)(seed + i);
        return salt;
    }

    private static SyncEnvelope NewEnvelope(string payloadBase64) => new()
    {
        Space = "sp_fixed_space",
        Version = 42,
        Base = 41,
        Device = "dev_fixed",
        Ts = "2026-09-11T09:36:15Z",
        Payload = payloadBase64,
    };



    [Fact]
    public void Canonical_UsesFixedFieldOrder_And_ExcludesMacAndPayload()
    {
        var text = System.Text.Encoding.UTF8.GetString(
            SyncEnvelopeCodec.Canonical(NewEnvelope("AAECAw==")));

        Assert.StartsWith("{\"sync\":1,\"crypto\":4,\"space\":\"sp_fixed_space\",\"version\":42,\"base\":41,", text);
        Assert.Contains("\"payloadSha256\":\"\"", text);
        Assert.DoesNotContain("\"mac\"", text);
        Assert.DoesNotContain("\"payload\"", text);
        Assert.DoesNotContain(" ", text);
    }

    [Fact]
    public void Canonical_IsByteStable()
    {
        var a = SyncEnvelopeCodec.Canonical(NewEnvelope("AAECAw=="));
        var b = SyncEnvelopeCodec.Canonical(NewEnvelope("AAECAw=="));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Seal_Then_Verify_Succeeds_And_Survives_JsonRoundTrip()
    {
        var key = FixedKey();
        var envelope = NewEnvelope(Convert.ToBase64String(SyncContainer.Seal("{\"x\":1}"u8.ToArray(), key, FixedSalt())));
        SyncEnvelopeCodec.Seal(envelope, key);

        Assert.True(SyncEnvelopeCodec.Verify(envelope, key));

        var reparsed = SyncEnvelopeCodec.Deserialize(SyncEnvelopeCodec.Serialize(envelope));
        Assert.True(SyncEnvelopeCodec.Verify(reparsed, key));
        Assert.Equal(envelope.Mac, reparsed.Mac);
        Assert.Equal(envelope.PayloadSha256, reparsed.PayloadSha256);
        Assert.Equal(envelope.Payload, reparsed.Payload);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("base")]
    [InlineData("device")]
    [InlineData("ts")]
    [InlineData("space")]
    [InlineData("sync")]
    [InlineData("crypto")]
    public void Verify_Rejects_TamperedMetadata(string field)
    {
        var key = FixedKey();
        var envelope = NewEnvelope(Convert.ToBase64String(SyncContainer.Seal("{}"u8.ToArray(), key, FixedSalt())));
        SyncEnvelopeCodec.Seal(envelope, key);
        Assert.True(SyncEnvelopeCodec.Verify(envelope, key));

        switch (field)
        {
            case "version": envelope.Version = 43; break;
            case "base": envelope.Base = 40; break;
            case "device": envelope.Device = "dev_other"; break;
            case "ts": envelope.Ts = "2026-09-11T09:36:16Z"; break;
            case "space": envelope.Space = "sp_other"; break;
            case "sync": envelope.Sync = 2; break;
            case "crypto": envelope.Crypto = 5; break;
        }

        Assert.False(SyncEnvelopeCodec.Verify(envelope, key));
    }

    [Fact]
    public void Verify_Rejects_SwappedPayload_And_WrongKey_And_GarbageMac()
    {
        var key = FixedKey();
        var envelope = NewEnvelope(Convert.ToBase64String(SyncContainer.Seal("{\"a\":1}"u8.ToArray(), key, FixedSalt())));
        SyncEnvelopeCodec.Seal(envelope, key);

        var swapped = NewEnvelope(Convert.ToBase64String(SyncContainer.Seal("{\"b\":2}"u8.ToArray(), key, FixedSalt())));
        swapped.Version = envelope.Version;
        swapped.Base = envelope.Base;
        swapped.Device = envelope.Device;
        swapped.Ts = envelope.Ts;
        swapped.Space = envelope.Space;
        swapped.Mac = envelope.Mac;
        swapped.PayloadSha256 = envelope.PayloadSha256;
        Assert.False(SyncEnvelopeCodec.Verify(swapped, key));

        Assert.False(SyncEnvelopeCodec.Verify(envelope, FixedKey(seed: 0x40)));

        envelope.Mac = "!!!not-base64!!!";
        Assert.False(SyncEnvelopeCodec.Verify(envelope, key));
    }

    [Fact]
    public void Deserialize_FailsClosed_On_MissingField_And_UnsupportedVersion()
    {
        Assert.Throws<InvalidDataException>(() => SyncEnvelopeCodec.Deserialize("{\"sync\":1}"));
        Assert.Throws<InvalidDataException>(() => SyncEnvelopeCodec.Deserialize("[]"));

        var key = FixedKey();
        var envelope = NewEnvelope(Convert.ToBase64String(SyncContainer.Seal("{}"u8.ToArray(), key, FixedSalt())));
        SyncEnvelopeCodec.Seal(envelope, key);
        var json = SyncEnvelopeCodec.Serialize(envelope).Replace("\"sync\":1", "\"sync\":2");
        Assert.Throws<InvalidDataException>(() => SyncEnvelopeCodec.Deserialize(json));
    }

    [Fact]
    public void UtcStamp_IsSecondPrecision_And_Parses()
    {
        var stamp = SyncEnvelopeCodec.UtcStamp(new DateTimeOffset(2026, 9, 11, 9, 36, 15, TimeSpan.FromHours(8)));
        Assert.Equal("2026-09-11T01:36:15Z", stamp);
        Assert.True(SyncEnvelopeCodec.TryParseStamp(stamp, out var parsed));
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
    }



    [Fact]
    public void SpaceKey_IsCanonicalBase64Text_And_Unique()
    {
        var a = SyncKeyWrap.CreateSpaceKey();
        var b = SyncKeyWrap.CreateSpaceKey();
        Assert.Equal(SyncKeyWrap.SpaceKeyTextLength, a.Length);
        Assert.Equal(32, Convert.FromBase64String(a).Length);
        Assert.NotEqual(a, b);
        Assert.True(SyncKeyWrap.IsWellFormedSpaceKey(a));
        Assert.False(SyncKeyWrap.IsWellFormedSpaceKey("not-base64!!"));
        Assert.False(SyncKeyWrap.IsWellFormedSpaceKey(Convert.ToBase64String(new byte[16])));
        Assert.False(SyncKeyWrap.IsWellFormedSpaceKey(""));
    }

    [Fact]
    public void KeyWrap_RoundTrips_And_Rejects_WrongPassword_And_Tampering()
    {
        var spaceKey = FixedKey(seed: 0x11);
        var password = "lock-pw-123";

        var record = SyncKeyWrap.Wrap(spaceKey, password);
        Assert.Equal(SyncKeyWrap.WrapVersion, record.Wrap);
        Assert.Equal(SyncKeyWrap.WrapIterations, record.Iter);
        Assert.Equal(spaceKey, SyncKeyWrap.Unwrap(record, password));

        Assert.Throws<InvalidDataException>(() => SyncKeyWrap.Unwrap(record, "other-pw"));

        var tamperedCt = SyncKeyWrap.Wrap(spaceKey, password);
        var ct = Convert.FromBase64String(tamperedCt.Ct);
        ct[0] ^= 0xFF;
        tamperedCt.Ct = Convert.ToBase64String(ct);
        Assert.Throws<InvalidDataException>(() => SyncKeyWrap.Unwrap(tamperedCt, password));

        var tamperedIter = SyncKeyWrap.Wrap(spaceKey, password);
        tamperedIter.Iter = SyncKeyWrap.WrapIterations - 1;
        Assert.Throws<InvalidDataException>(() => SyncKeyWrap.Unwrap(tamperedIter, password));

        var tamperedSalt = SyncKeyWrap.Wrap(spaceKey, password);
        tamperedSalt.Salt = Convert.ToBase64String(new byte[SyncKeyWrap.SaltSize]);
        Assert.Throws<InvalidDataException>(() => SyncKeyWrap.Unwrap(tamperedSalt, password));
    }

    [Fact]
    public void KeyWrap_Rejects_UnsupportedIdentifiers_And_ShortRecords()
    {
        var record = SyncKeyWrap.Wrap(FixedKey(), "pw");
        var badWrap = SyncKeyWrap.Wrap(FixedKey(), "pw");
        badWrap.Wrap = 2;
        Assert.Throws<InvalidDataException>(() => SyncKeyWrap.Unwrap(badWrap, "pw"));

        var badKdf = SyncKeyWrap.Wrap(FixedKey(), "pw");
        badKdf.Kdf = "argon2id";
        Assert.Throws<InvalidDataException>(() => SyncKeyWrap.Unwrap(badKdf, "pw"));

        var badLen = SyncKeyWrap.Wrap(FixedKey(), "pw");
        badLen.Ct = Convert.ToBase64String(new byte[8]);
        Assert.Throws<InvalidDataException>(() => SyncKeyWrap.Unwrap(badLen, "pw"));

        Assert.Equal(SyncKeyWrap.WrapIterations, record.Iter);
    }

    [Fact]
    public void BlobKey_IsDeterministic_And_SaltSensitive()
    {
        var spaceKey = FixedKey();
        var a = SyncKeyWrap.DeriveBlobKey(spaceKey, FixedSalt());
        var b = SyncKeyWrap.DeriveBlobKey(spaceKey, FixedSalt());
        var c = SyncKeyWrap.DeriveBlobKey(spaceKey, FixedSalt(seed: 0x30));

        Assert.Equal(32, a.Length);
        Assert.Equal(Convert.ToHexString(a), Convert.ToHexString(b));
        Assert.NotEqual(Convert.ToHexString(a), Convert.ToHexString(c));
        Assert.Throws<ArgumentException>(() => SyncKeyWrap.DeriveBlobKey("not-a-space-key", FixedSalt()));
        Assert.Throws<ArgumentException>(() => SyncKeyWrap.DeriveBlobKey(spaceKey, new byte[16]));
    }



    [Fact]
    public void Container_RoundTrips_And_Declares_V4_WithBlobIterations()
    {
        var spaceKey = FixedKey();
        var salt = SyncContainer.NewVersionSalt();
        var plain = "{\"hello\":\"世界\"}"u8.ToArray();

        var container = SyncContainer.Seal(plain, spaceKey, salt);
        Assert.Equal(Convert.ToBase64String(plain), Convert.ToBase64String(SyncContainer.Open(container, spaceKey)));

        Assert.True(SyncContainer.TryReadHeader(container, out var version, out var iterations, out var readSalt));
        Assert.Equal(4, version);
        Assert.Equal(SyncKeyWrap.BlobIterations, iterations);
        Assert.Equal(Convert.ToBase64String(salt), Convert.ToBase64String(readSalt));
        Assert.True(iterations >= 1000);
    }

    [Fact]
    public void Container_UsesFreshSaltPerVersion()
    {
        var spaceKey = FixedKey();
        var a = SyncContainer.Seal("{}"u8.ToArray(), spaceKey, SyncContainer.NewVersionSalt());
        var b = SyncContainer.Seal("{}"u8.ToArray(), spaceKey, SyncContainer.NewVersionSalt());
        Assert.NotEqual(Convert.ToBase64String(a.AsSpan(44).ToArray()), Convert.ToBase64String(b.AsSpan(44).ToArray()));
        Assert.NotEqual(Convert.ToBase64String(a.AsSpan(12, 32).ToArray()), Convert.ToBase64String(b.AsSpan(12, 32).ToArray()));
    }

    [Fact]
    public void Container_FailsClosed_On_WrongKey_Truncation_And_HeaderDamage()
    {
        var spaceKey = FixedKey();
        var container = SyncContainer.Seal("{}"u8.ToArray(), spaceKey, SyncContainer.NewVersionSalt());

        Assert.Throws<InvalidDataException>(() => SyncContainer.Open(container, FixedKey(seed: 0x40)));
        Assert.Throws<InvalidDataException>(() => SyncContainer.Open(container.AsSpan(0, 44).ToArray(), spaceKey));

        void AssertHeaderRejected(Action<byte[]> mutate)
        {
            var copy = container.ToArray();
            mutate(copy);
            Assert.Throws<InvalidDataException>(() => SyncContainer.Open(copy, spaceKey));
        }

        AssertHeaderRejected(c => c[0] ^= 0xFF);
        AssertHeaderRejected(c => c[4] = 3);
        AssertHeaderRejected(c => c[5] = 0x00);
        AssertHeaderRejected(c => c[6] = 0x09);
        AssertHeaderRejected(c => c[7] = 0x09);
        AssertHeaderRejected(c => BitConverter.GetBytes(999).CopyTo(c, 8));
        AssertHeaderRejected(c => BitConverter.GetBytes(9_000_000).CopyTo(c, 8));
        AssertHeaderRejected(c => c[43] ^= 0xFF);
    }



    private static NovaraDatabase SampleDatabase()
    {
        var db = new NovaraDatabase();
        db.AppSettings.AppLanguage = "en-US";
        db.AppSettings.Theme = "深色";
        db.AppSettings.McpToken = "mcp-token-secret";
        db.AppSettings.McpEnabled = true;
        db.AppSettings.McpAllowedProcesses.Add(@"C:\tools\agent.exe");
        db.AppSettings.McpClientPermissions.Add(new McpClientPermRecord { Path = @"C:\tools\agent.exe", Permissions = 1 });
        db.AppSettings.AutoStart = true;
        db.AppSettings.ContextMenu = false;
        db.AppSettings.CloseBehavior = "最小化到托盘";
        db.AppSettings.BackupEnabled = true;
        db.AppSettings.QuickCaptureHotkey = "AltN";
        db.AppSettings.VisibleTabs = new List<string> { "备忘" };
        db.AppSettings.PrivacyLockEnabled = true;
        db.AppSettings.HasCompletedWelcome = true;

        db.MemoGroups.Add(new MemoGroup { Name = "组" });
        db.MemoEntries.Add(new MemoEntry { Name = "活动条目", Type = "自定义" });
        db.MemoEntries.Add(new MemoEntry { Name = "已删条目", Type = "自定义", IsDeleted = true, DeletedAt = DateTime.Now });
        db.PathBackupItems.Add(new FilePathEntry { Name = "路径", Path = @"C:\x" });
        db.TodoCards.Add(new TodoCard { Title = "待办" });
        db.NoteCards.Add(new NoteCard { Title = "便签" });
        db.DiaryItems.Add(new DiaryEntry { Title = "日记", Content = "<p>hi</p>" });
        return db;
    }

    [Fact]
    public void CloneForSync_KeepsDataAndTombstones_But_StripsCredentialsAndLocalSwitches()
    {
        var source = SampleDatabase();
        var clone = SyncFieldPolicy.CloneForSync(source);

        Assert.Single(clone.MemoGroups);
        Assert.Equal(2, clone.MemoEntries.Count);
        Assert.Contains(clone.MemoEntries, e => e.IsDeleted);
        Assert.Single(clone.PathBackupItems);
        Assert.Single(clone.TodoCards);
        Assert.Single(clone.NoteCards);
        Assert.Single(clone.DiaryItems);

        Assert.Equal("en-US", clone.AppSettings.AppLanguage);
        Assert.Equal("深色", clone.AppSettings.Theme);

        Assert.Equal("", clone.AppSettings.McpToken);
        Assert.False(clone.AppSettings.McpEnabled);
        Assert.Empty(clone.AppSettings.McpAllowedProcesses);
        Assert.Empty(clone.AppSettings.McpClientPermissions);
        Assert.False(clone.AppSettings.AutoStart);
        Assert.True(clone.AppSettings.ContextMenu);
        Assert.NotEqual("最小化到托盘", clone.AppSettings.CloseBehavior);
        Assert.False(clone.AppSettings.BackupEnabled);
        Assert.NotEqual("AltN", clone.AppSettings.QuickCaptureHotkey);
        Assert.NotEqual(new List<string> { "备忘" }, clone.AppSettings.VisibleTabs);
        Assert.False(clone.AppSettings.PrivacyLockEnabled);
        Assert.False(clone.AppSettings.HasCompletedWelcome);
    }

    [Fact]
    public void CloneForSync_IsDeep_And_DoesNotDisturbTheSource()
    {
        var source = SampleDatabase();
        var clone = SyncFieldPolicy.CloneForSync(source);

        clone.MemoEntries.Clear();
        clone.MemoEntries.Add(new MemoEntry { Name = "新条目", Type = "自定义" });
        clone.DiaryItems[0].Content = "changed";
        clone.AppSettings.AppLanguage = "ja-JP";

        Assert.Equal(2, source.MemoEntries.Count);
        Assert.Equal("<p>hi</p>", source.DiaryItems[0].Content);
        Assert.Equal("en-US", source.AppSettings.AppLanguage);
        Assert.Equal("mcp-token-secret", source.AppSettings.McpToken);
    }

    [Fact]
    public void ApplyRoamingSettings_UpdatesOnlyRoamingFields()
    {
        var local = new AppSettings
        {
            AppLanguage = "zh-CN",
            Theme = "浅色",
            AutoStart = true,
            QuickCaptureHotkey = "AltN",
            VisibleTabs = new List<string> { "备忘", "计划" },
        };
        var remote = new AppSettings { AppLanguage = "en-US", Theme = "深色", AutoStart = false, QuickCaptureHotkey = "CtrlShiftN" };

        var result = SyncFieldPolicy.ApplyRoamingSettings(local, remote);

        Assert.Same(local, result);
        Assert.Equal("en-US", local.AppLanguage);
        Assert.Equal("深色", local.Theme);
        Assert.True(local.AutoStart);
        Assert.Equal("AltN", local.QuickCaptureHotkey);
        Assert.Equal(new List<string> { "备忘", "计划" }, local.VisibleTabs);
    }



    [Fact]
    public void SyncState_RoundTrips_And_DegradesSafely()
    {
        var state = new SyncState
        {
            Enabled = true,
            ServerUrl = "https://novara.example.com",
            SpaceId = "sp_1",
            DeviceId = "dev_1",
            DeviceName = "台式机",
            FrequencyMinutes = SyncState.FrequencyManual,
            LastSeenVersion = 42,
            BaseVersion = 42,
            LastPushedSha256 = "abc",
            LastSyncAt = new DateTime(2026, 9, 11, 9, 36, 15, DateTimeKind.Utc),
        };

        var back = SyncJson.DeserializeState(SyncJson.SerializeState(state));
        Assert.Equal(state.ServerUrl, back.ServerUrl);
        Assert.Equal(state.SpaceId, back.SpaceId);
        Assert.Equal(SyncState.FrequencyManual, back.FrequencyMinutes);
        Assert.Equal(42, back.LastSeenVersion);
        Assert.Equal(state.LastSyncAt, back.LastSyncAt);

        Assert.False(SyncJson.DeserializeState(null).Enabled);
        Assert.Equal(5, SyncJson.DeserializeState("").FrequencyMinutes);
        Assert.Equal(5, SyncJson.DeserializeState("{ not json").FrequencyMinutes);
    }

    [Fact]
    public void KeyWrapRecord_SurvivesJsonRoundTrip_And_StillUnwraps()
    {
        var spaceKey = FixedKey(seed: 0x11);
        var record = SyncKeyWrap.Wrap(spaceKey, "lock-pw");
        var back = SyncJson.DeserializeKeyWrap(SyncJson.SerializeKeyWrap(record));

        Assert.Equal(record.Wrap, back.Wrap);
        Assert.Equal(record.Iter, back.Iter);
        Assert.Equal(spaceKey, SyncKeyWrap.Unwrap(back, "lock-pw"));

        Assert.Throws<InvalidDataException>(() => SyncJson.DeserializeKeyWrap(""));
        Assert.Throws<InvalidDataException>(() => SyncJson.DeserializeKeyWrap("{ not json"));
    }
}
