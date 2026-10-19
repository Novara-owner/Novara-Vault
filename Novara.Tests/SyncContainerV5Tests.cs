using System.Security.Cryptography;
using System.Text.Json;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SyncContainerV5Tests
{
    private static readonly string SpaceKey = Convert.ToBase64String(System.Linq.Enumerable.Repeat((byte)0xAB, 32).ToArray().ToArray());

    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    [Fact]
    public void ManifestContainer_RoundTrips_AndRejectsWrongKey()
    {
        var manifest = System.Text.Encoding.UTF8.GetBytes("{\"format\":5,\"partitions\":{\"memoEntries\":\"AA\"}}");
        var salt = SyncContainer.NewVersionSalt();

        var container = SyncContainer.SealManifest(manifest, SpaceKey, salt);
        Assert.Equal(5, container[4]);

        Assert.Equal(manifest, SyncContainer.OpenManifest(container, SpaceKey));
        var wrongKey = Convert.ToBase64String(System.Linq.Enumerable.Repeat((byte)0xCD, 32).ToArray());
        Assert.Throws<InvalidDataException>(() => SyncContainer.OpenManifest(container, wrongKey));
    }

    [Fact]
    public void ManifestFixture_SealedByDesktop_DecryptsToPinnedJson()
    {



        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Novara.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var b64 = File.ReadAllText(Path.Combine(root!.FullName, "Tools", "fixtures", "v5-manifest.b64")).Trim();
        var expected = File.ReadAllText(Path.Combine(root.FullName, "Tools", "fixtures", "v5-manifest.json")).Trim();
        var key = Convert.ToBase64String(System.Linq.Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

        var opened = SyncContainer.OpenManifest(Convert.FromBase64String(b64), key);
        Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(opened));
    }

    [Fact]
    public void ManifestContainer_RejectsV4ContainersAsManifests()
    {
        var v4 = SyncContainer.Seal(System.Text.Encoding.UTF8.GetBytes("{}"), SpaceKey, SyncContainer.NewVersionSalt());

        Assert.Throws<InvalidDataException>(() => SyncContainer.OpenManifest(v4, SpaceKey));
    }

    [Fact]
    public void Chunk_SealIsDeterministic_AndDependsOnPartitionContent()
    {
        var json = System.Text.Encoding.UTF8.GetBytes("[{\"name\":\"a\"}]");
        var pid = SyncContainer.PartitionPid(json);

        var blob1 = SyncContainer.SealChunk(json, SpaceKey, pid);
        var blob2 = SyncContainer.SealChunk(json, SpaceKey, pid);
        Assert.Equal(blob1, blob2);

        var other = SyncContainer.SealChunk(System.Text.Encoding.UTF8.GetBytes("[{\"name\":\"b\"}]"), SpaceKey, SyncContainer.PartitionPid(System.Text.Encoding.UTF8.GetBytes("[{\"name\":\"b\"}]")));
        Assert.NotEqual(blob1, other);
    }

    [Fact]
    public void Chunk_OpenRoundTrips_AndRejectsTampering()
    {
        var json = System.Text.Encoding.UTF8.GetBytes("[{\"name\":\"tamper\"}]");
        var pid = SyncContainer.PartitionPid(json);
        var blob = SyncContainer.SealChunk(json, SpaceKey, pid);

        Assert.Equal(json, SyncContainer.OpenChunk(blob, SpaceKey, pid));

        var tampered = (byte[])blob.Clone();
        tampered[^1] ^= 0x01;
        Assert.Throws<InvalidDataException>(() => SyncContainer.OpenChunk(tampered, SpaceKey, pid));


        var wrongKey = Convert.ToBase64String(System.Linq.Enumerable.Repeat((byte)0xCD, 32).ToArray());
        Assert.Throws<InvalidDataException>(() => SyncContainer.OpenChunk(blob, wrongKey, pid));
    }

    [Fact]
    public void ChunkKeyAndNonce_MatchesTheCrossLanguageVector()
    {



        var spaceKey = Convert.ToBase64String(System.Linq.Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        var pid = System.Linq.Enumerable.Range(0x20, 32).Select(i => (byte)i).ToArray();

        var (key, nonce) = SyncContainer.ChunkKeyAndNonce(spaceKey, pid);

        Assert.Equal("55be8595232449d53df05cf6e0584abbc7a3542559502cc9cbb986383f9cc092",
            Convert.ToHexString(key).ToLowerInvariant());
        Assert.Equal("7db4e4d976324eeaeb30325c", Convert.ToHexString(nonce).ToLowerInvariant());
    }



    [Fact]
    public void ApplyPartitions_CorruptPartition_FailsWithoutTouchingTheLibrary()
    {
        var db = new NovaraDatabase();
        db.TodoCards.Add(new TodoCard { Title = "本地待办" });
        var partitions = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var key in SyncPartitions.Keys)
            partitions[key] = System.Text.Encoding.UTF8.GetBytes(key == "noteCards"
                ? "not json"
                : "\"null\"");

        var target = new NovaraDatabase();
        target.TodoCards.Add(new TodoCard { Title = "本地待办" });
        target.NoteCards.Add(new NoteCard { Title = "本地便签" });

        Assert.ThrowsAny<Exception>(() => SyncPartitions.ApplyPartitions(target, partitions));

        Assert.Single(target.TodoCards);
        Assert.Equal("本地待办", target.TodoCards[0].Title);
        Assert.Single(target.NoteCards);
        Assert.Equal("本地便签", target.NoteCards[0].Title);
        Assert.Empty(target.DiaryItems);
        Assert.Empty(target.MemoGroups);
    }





    [Fact]
    public void ApplyPartitions_NormalizesDuplicateIdsAndDanglingRefs()
    {
        var partitions = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var key in SyncPartitions.Keys)
            partitions[key] = System.Text.Encoding.UTF8.GetBytes("null");
        partitions["memoGroups"] = System.Text.Encoding.UTF8.GetBytes(
            "[{\"id\":\"44444444-4444-4444-4444-444444444444\",\"name\":\"A\"},{\"id\":\"44444444-4444-4444-4444-444444444444\",\"name\":\"B\"}]");
        partitions["memoEntries"] = System.Text.Encoding.UTF8.GetBytes(
            "[{\"id\":\"55555555-5555-5555-5555-555555555555\",\"type\":\"自定义\",\"name\":\"E\",\"groupId\":\"66666666-6666-6666-6666-666666666666\"}]");

        var target = new NovaraDatabase();
        SyncPartitions.ApplyPartitions(target, partitions);

        Assert.Equal(2, target.MemoGroups.Count);
        Assert.NotEqual(target.MemoGroups[0].Id, target.MemoGroups[1].Id);
        var entry = Assert.Single(target.MemoEntries);
        Assert.Null(entry.GroupId);
    }

    [Fact]
    public void ExtractAndApplyPartitions_RoundTripsAllSeven()
    {
        var db = new NovaraDatabase();
        db.AppSettings.AppLanguage = "zh-CN";
        db.MemoGroups.Add(new MemoGroup { Name = "组" });
        db.MemoEntries.Add(new MemoEntry { Name = "条目", Type = "自定义" });
        db.PathBackupItems.Add(new FilePathEntry { Name = "路径", Path = "C:\\" });
        db.TodoCards.Add(new TodoCard { Title = "待办" });
        db.TodoCards[0].SubTexts.Add("子项");
        db.NoteCards.Add(new NoteCard { Title = "便签", Content = "内容" });
        db.DiaryItems.Add(new DiaryEntry { Title = "日记", Content = "<p>日记正文</p>", Format = "html" });

        var stable = StableJson.SerializeToUtf8BytesStable(db, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                   ?? throw new InvalidOperationException("stable serialization failed");
        var partitions = SyncPartitions.ExtractPartitions(stable);
        Assert.Equal(7, partitions.Count);


        Assert.Equal(SyncPartitions.Keys.Length, partitions.Count);


        var target = new NovaraDatabase();
        SyncPartitions.ApplyPartitions(target, partitions);
        var stable2 = StableJson.SerializeToUtf8BytesStable(target, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                    ?? throw new InvalidOperationException("stable serialization failed");
        var partitions2 = SyncPartitions.ExtractPartitions(stable2);


        SyncPartitions.ApplyPartitions(target, partitions2);
        var stable3 = StableJson.SerializeToUtf8BytesStable(target, Opts)
                    ?? throw new InvalidOperationException("stable serialization failed");
        var partitions3 = SyncPartitions.ExtractPartitions(stable3);
        foreach (var key in SyncPartitions.Keys)
            Assert.True(SyncPartitions.PidOf(partitions2[key]) == SyncPartitions.PidOf(partitions3[key]),
                "分区 " + key + " 二次应用后不一致（应用必须幂等）。源: " + System.Text.Encoding.UTF8.GetString(partitions2[key]) + " 标: " + System.Text.Encoding.UTF8.GetString(partitions3[key]));


        Assert.Equal("条目", target.MemoEntries.Single().Name);
        Assert.Equal("待办", target.TodoCards.Single().Title);
        Assert.Equal("子项", target.TodoCards.Single().SubTexts.Single());
        Assert.Single(target.MemoGroups);
    }
}
