using System.Text;
using System.Text.Json;
using Novara.Models;
using Novara.Server;
using Novara.Services;
using Novara.Sync.Server;
using Novara.Sync.Server.Storage;
using Xunit;

namespace Novara.Tests;

public class SpaceAdminTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "novara-admin-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }




    private ISpaceStore NewStore(string backend) => backend == "file"
        ? new FileSpaceStore(_dir)
        : new SqliteSpaceStore(_dir);

    private SpaceService NewService(string backend) => new(NewStore(backend), new SyncServerOptions
    {
        StorageRoot = _dir,
        DefaultQuotaBytes = 500L * 1024 * 1024,
        DefaultMaxVersions = 10,
    });

    private string SpaceDirOf(string spaceId) => Path.Combine(_dir, "spaces", spaceId);

    private static SpaceRecord Space(string id, string name, int day) => new()
    {
        SpaceId = id,
        Name = name,
        CreatedAt = new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc),
    };

    private static string SealedEnvelope(string spaceKey, string spaceId, string deviceId, long baseVersion)
    {
        var container = SyncContainer.Seal(Encoding.UTF8.GetBytes("{\"k\":\"v\"}"), spaceKey, SyncContainer.NewVersionSalt());
        var envelope = new SyncEnvelope
        {
            Space = spaceId,
            Device = deviceId,
            Base = baseVersion,
            Version = baseVersion + 1,
            Ts = SyncEnvelopeCodec.UtcStamp(DateTimeOffset.UtcNow),
            Payload = Convert.ToBase64String(container),
        };
        SyncEnvelopeCodec.Seal(envelope, spaceKey);
        return SyncEnvelopeCodec.Serialize(envelope);
    }





    private static (int Code, string Out, string Err) RunCli(SpaceService service, params string[] args)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        try
        {
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            return (Cli.RunSpaceCommand(service, args), outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }



    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void ListSpaces_IsEmptyOnAFreshStore(string backend)
        => Assert.Empty(NewStore(backend).ListSpaces());

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void ListSpaces_ReturnsEverySpace_OldestFirst(string backend)
    {
        var store = NewStore(backend);
        store.SaveSpace(Space("b-second", "second", 2));
        store.SaveSpace(Space("a-first", "first", 1));

        var spaces = store.ListSpaces();

        Assert.Equal(new[] { "a-first", "b-second" }, spaces.Select(s => s.SpaceId).ToArray());
        Assert.Equal("first", spaces[0].Name);

        Assert.Equal(2, spaces.Count);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void DeleteSpace_RemovesMetadataAndPayloads_ButLeavesItsNeighbourAlone(string backend)
    {
        var store = NewStore(backend);
        store.SaveSpace(Space("keep", "keep", 1));
        store.SaveSpace(Space("drop", "drop", 2));
        store.SaveDevice("drop", new DeviceRecord { DeviceId = "d1", Name = "pc", TokenHash = "h1" });
        store.SaveVersions("drop", new[] { new VersionRecord { Version = 1, DeviceId = "d1", Sha256 = "a", Size = 3 } });
        store.WriteBlob("drop", new VersionRecord { Version = 1, Size = 3 }, new byte[] { 1, 2, 3 });
        Assert.True(File.Exists(Path.Combine(SpaceDirOf("drop"), "blobs", "1")));

        store.DeleteSpace("drop");

        Assert.Null(store.GetSpace("drop"));
        Assert.False(store.SpaceExists("drop"));
        Assert.Empty(store.GetVersions("drop"));
        Assert.Empty(store.GetDevices("drop"));

        Assert.False(Directory.Exists(SpaceDirOf("drop")));

        Assert.NotNull(store.GetSpace("keep"));
        Assert.True(store.SpaceExists("keep"));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void DeleteSpace_IsIdempotentAtTheStoreLayer(string backend)
    {
        var store = NewStore(backend);
        store.SaveSpace(Space("sp1", "one", 1));

        store.DeleteSpace("sp1");


        store.DeleteSpace("sp1");

        Assert.Null(store.GetSpace("sp1"));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void DeleteSpace_RejectsUnsafeIds(string backend)
    {
        var store = NewStore(backend);
        Assert.Throws<SpaceStoreException>(() => store.DeleteSpace("../escape"));
        Assert.Throws<SpaceStoreException>(() => store.DeleteSpace("a/b"));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void WritePaths_RefuseToResurrectADeletedSpace(string backend)
    {
        var store = NewStore(backend);
        store.SaveSpace(Space("gone", "gone", 1));
        store.SaveVersions("gone", Array.Empty<VersionRecord>());
        store.DeleteSpace("gone");




        Assert.Throws<SpaceGoneException>(() => store.SaveSpace(Space("gone", "gone", 1), requireExisting: true));
        Assert.Throws<SpaceGoneException>(() => store.SaveVersions("gone", new List<VersionRecord>
            { new() { Version = 1, CreatedAt = DateTime.UtcNow, DeviceId = "d", Sha256 = "x", Size = 1 } }));
        Assert.Throws<SpaceGoneException>(() => store.SaveDevice("gone",
            new DeviceRecord { DeviceId = "d", Name = "n", CreatedAt = DateTime.UtcNow }));
        Assert.Null(store.GetSpace("gone"));
        Assert.DoesNotContain(store.ListSpaces(), s => s.SpaceId == "gone");


        store.SaveSpace(Space("gone2", "gone2", 2));
        Assert.NotNull(store.GetSpace("gone2"));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void Cli_AcceptsSpaceIdsWithLeadingDash(string backend)
    {


        var store = NewStore(backend);
        var id = "--" + new string('a', 20);
        store.SaveSpace(Space(id, "dash", 1));

        var (showCode, showOut, _) = RunCli(NewService(backend), "space", "show", id);
        Assert.Equal(0, showCode);
        Assert.Contains("dash", showOut);

        var (delCode, _, _) = RunCli(NewService(backend), "space", "delete", id, "--yes");
        Assert.Equal(0, delCode);
        Assert.Null(store.GetSpace(id));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void ListSpaces_IgnoresDirectoriesThatAreNotSpaces(string backend)
    {
        var store = NewStore(backend);
        store.SaveSpace(Space("real", "real", 1));

        Directory.CreateDirectory(Path.Combine(_dir, "spaces", "not a space"));

        var spaces = store.ListSpaces();

        Assert.Equal(new[] { "real" }, spaces.Select(s => s.SpaceId).ToArray());
    }

    [Fact]
    public void ListSpaces_FileBackend_RaisesOnCorruptMetadataRatherThanSkippingIt()
    {
        var store = new FileSpaceStore(_dir);
        store.SaveSpace(Space("sp1", "one", 1));
        File.WriteAllText(Path.Combine(SpaceDirOf("sp1"), "space.json"), "{ this is not json");



        Assert.Throws<SpaceStoreException>(() => store.ListSpaces());
    }

    [Fact]
    public void ListSpaces_SqliteBackend_RaisesOnADamagedDatabase()
    {
        var store = new SqliteSpaceStore(_dir);
        store.SaveSpace(Space("sp1", "one", 1));

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = Path.Combine(_dir, "novara-sync.db" + suffix);
            if (File.Exists(path)) File.Delete(path);
        }
        File.WriteAllBytes(Path.Combine(_dir, "novara-sync.db"), Enumerable.Repeat((byte)0x41, 4096).ToArray());

        Assert.Throws<SpaceStoreException>(() => new SqliteSpaceStore(_dir).ListSpaces());
    }



    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void ListSpaces_SummarisesEachSpaceForTheOperator(string backend)
    {
        var service = NewService(backend);
        var store = NewStore(backend);
        var created = service.CreateSpace("mine");
        var spaceId = created.Value!.SpaceId;

        var spaceKey = SyncKeyWrap.CreateSpaceKey();
        var deviceId = service.RegisterDevice(spaceId, created.Value.EnrollmentSecret, "pc").Value!.DeviceId;
        var device = store.GetDevice(spaceId, deviceId)!;
        service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0, false);

        var summary = Assert.Single(service.ListSpaces().Value!);

        Assert.Equal(spaceId, summary.SpaceId);
        Assert.Equal("mine", summary.Name);
        Assert.Equal(1, summary.CurrentVersion);
        Assert.Equal(1, summary.DeviceCount);
        Assert.Equal(0, summary.RevokedDeviceCount);
        Assert.False(summary.HasReadToken);
        Assert.True(summary.UsedBytes > 0);
        Assert.Equal(500L * 1024 * 1024, summary.QuotaBytes);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void DescribeSpace_ReportsNotFoundForAnUnknownId(string backend)
    {
        var service = NewService(backend);
        var result = service.DescribeSpace("nope");
        Assert.False(result.Success);
        Assert.Equal(SyncErrorCode.SpaceNotFound, result.Error);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void DeleteSpace_ReturnsWhatItRemoved_ThenReportsNotFound(string backend)
    {
        var service = NewService(backend);
        var created = service.CreateSpace("mine");
        var spaceId = created.Value!.SpaceId;
        Assert.True(service.RegisterDevice(spaceId, created.Value.EnrollmentSecret, "pc").Success);

        var removed = service.DeleteSpace(spaceId);

        Assert.True(removed.Success);


        Assert.Equal(spaceId, removed.Value!.SpaceId);
        Assert.Equal("mine", removed.Value.Name);
        Assert.Equal(1, removed.Value.DeviceCount);

        Assert.Equal(SyncErrorCode.SpaceNotFound, service.DeleteSpace(spaceId).Error);
        Assert.Equal(SyncErrorCode.SpaceNotFound, service.DescribeSpace(spaceId).Error);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void RotateEnrollmentSecret_StopsTheOldSecret_ButKeepsPairedDevicesWorking(string backend)
    {
        var service = NewService(backend);
        var store = NewStore(backend);

        var created = service.CreateSpace("mine");
        var spaceId = created.Value!.SpaceId;
        var oldSecret = created.Value.EnrollmentSecret;
        var spaceKey = SyncKeyWrap.CreateSpaceKey();

        var registered = service.RegisterDevice(spaceId, oldSecret, "pc");
        var deviceId = registered.Value!.DeviceId;
        var token = registered.Value.DeviceToken;



        var device = store.GetDevice(spaceId, deviceId)!;
        service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0, false);
        Assert.Equal(1, store.GetSpace(spaceId)!.CurrentVersion);

        var rotated = service.RotateEnrollmentSecret(spaceId);

        Assert.True(rotated.Success);
        Assert.Equal(spaceId, rotated.Value!.SpaceId);
        Assert.NotEqual(oldSecret, rotated.Value.EnrollmentSecret);
        Assert.Equal(1, store.GetSpace(spaceId)!.CurrentVersion);

        Assert.Equal(SyncErrorCode.Forbidden, service.RegisterDevice(spaceId, oldSecret, "pc2").Error);
        Assert.True(service.RegisterDevice(spaceId, rotated.Value.EnrollmentSecret, "pc2").Success);


        Assert.True(service.Authorize(spaceId, deviceId, token).Success);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void RotateEnrollmentSecret_ReportsNotFoundForAnUnknownId(string backend)
    {
        var service = NewService(backend);
        var result = service.RotateEnrollmentSecret("nope");
        Assert.False(result.Success);
        Assert.Equal(SyncErrorCode.SpaceNotFound, result.Error);
    }



    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void Cli_DeleteWithoutYes_Refuses_AndLeavesTheSpaceInPlace(string backend)
    {
        var service = NewService(backend);
        var spaceId = service.CreateSpace("mine").Value!.SpaceId;

        var (code, _, err) = RunCli(service, "space", "delete", spaceId);

        Assert.Equal(2, code);
        Assert.Contains("refusing to delete without --yes", err);

        Assert.Contains(spaceId, err);
        Assert.True(service.DescribeSpace(spaceId).Success);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void Cli_DeleteWithYes_RemovesTheSpace_AndASecondRunReportsNotFound(string backend)
    {
        var service = NewService(backend);
        var spaceId = service.CreateSpace("mine").Value!.SpaceId;

        var (code, output, _) = RunCli(service, "space", "delete", spaceId, "--yes");

        Assert.Equal(0, code);
        Assert.Contains(spaceId, output);
        Assert.False(service.DescribeSpace(spaceId).Success);

        var (second, _, secondErr) = RunCli(service, "space", "delete", spaceId, "--yes");
        Assert.Equal(1, second);
        Assert.Contains("failed: space not found", secondErr);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void Cli_MistypedFlagDoesNotQuietlyChangeWhatDeleteDoes(string backend)
    {
        var service = NewService(backend);
        var spaceId = service.CreateSpace("mine").Value!.SpaceId;



        var (code, _, err) = RunCli(service, "space", "delete", spaceId, "--force");

        Assert.Equal(2, code);
        Assert.Contains("unknown or incomplete option: --force", err);
        Assert.True(service.DescribeSpace(spaceId).Success);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void Cli_ListJsonIsMachineReadable(string backend)
    {
        var service = NewService(backend);
        var spaceId = service.CreateSpace("mine").Value!.SpaceId;

        var (code, output, _) = RunCli(service, "space", "list", "--json");

        Assert.Equal(0, code);
        using var document = JsonDocument.Parse(output);
        var entry = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(spaceId, entry.GetProperty("spaceId").GetString());
        Assert.Equal("mine", entry.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void Cli_ListOnAnEmptyServerSaysSoInsteadOfPrintingNothing(string backend)
    {
        var (code, output, _) = RunCli(NewService(backend), "space", "list");

        Assert.Equal(0, code);
        Assert.Contains("No spaces on this server yet.", output);

        Assert.Contains("NovaraSync space create", output);
    }

    [Fact]
    public void Cli_ReportsCommandLineErrorsAsExitTwo_AndBusinessFailuresAsExitOne()
    {
        var service = NewService("file");

        Assert.Equal(2, RunCli(service, "space").Code);
        Assert.Equal(2, RunCli(service, "space", "bogus").Code);
        Assert.Equal(2, RunCli(service, "space", "show").Code);
        Assert.Equal(1, RunCli(service, "space", "show", "nope").Code);
        Assert.Equal(1, RunCli(service, "space", "rotate-secret", "nope").Code);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void Cli_CreateThenListThenShow_AgreeOnTheSameSpace(string backend)
    {
        var service = NewService(backend);

        var (created, createOut, _) = RunCli(service, "space", "create", "--name", "验收空间");
        Assert.Equal(0, created);



        var idLine = createOut.Split('\n').First(l => l.Contains("space id"));
        var spaceId = idLine.Split(':')[1].Trim();

        var (listed, listOut, _) = RunCli(service, "space", "list");
        Assert.Equal(0, listed);
        Assert.Contains(spaceId, listOut);
        Assert.Contains("验收空间", listOut);






        var (shown, showOut, _) = RunCli(service, "space", "show", spaceId);
        Assert.Equal(0, shown);
        Assert.Contains("验收空间", showOut);
    }
}
