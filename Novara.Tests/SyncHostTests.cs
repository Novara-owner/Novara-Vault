using System.Net;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Novara.Models;
using Novara.Server;
using Novara.Services;
using Novara.Sync.Server;
using Xunit;

namespace Novara.Tests;

public class SyncHostTests
{


    [Theory]
    [InlineData(true, "127.0.0.1", false)]
    [InlineData(true, "203.0.113.7", false)]
    [InlineData(false, "127.0.0.1", false)]
    [InlineData(false, "::1", false)]
    [InlineData(false, "203.0.113.7", true)]
    [InlineData(false, "192.168.1.50", true)]
    public void RequiresSecureTransport_RefusesPlaintextOffLoopback(bool isHttps, string remote, bool expected)
        => Assert.Equal(expected, SyncHostSetup.RequiresSecureTransport(isHttps, IPAddress.Parse(remote)));

    [Fact]
    public void RequiresSecureTransport_TreatsAnUnknownPeerAsRemote()
        => Assert.True(SyncHostSetup.RequiresSecureTransport(isHttps: false, remoteAddress: null));



    [Fact]
    public void ValidateDataRoot_CreatesAMissingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "novara-dataroot-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "data");
        try
        {


            Assert.Null(SyncHostSetup.ValidateDataRoot(nested));
            Assert.True(Directory.Exists(nested));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void ValidateDataRoot_LeavesNoProbeFileBehind()
    {
        var root = Path.Combine(Path.GetTempPath(), "novara-dataroot-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(SyncHostSetup.ValidateDataRoot(root));


            Assert.Empty(Directory.GetFiles(root));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void ValidateDataRoot_RefusesAPathThatCannotBeCreated()
    {




        var file = Path.Combine(Path.GetTempPath(), "novara-dataroot-file-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(file, "not a directory");
            var problem = SyncHostSetup.ValidateDataRoot(Path.Combine(file, "data"));

            Assert.NotNull(problem);
            Assert.Contains("cannot be written", problem);




            if (OperatingSystem.IsWindows())
                Assert.Contains("NOVARA_SYNC_DATA", problem);
            else
                Assert.Contains("chown -R", problem);
        }
        finally
        {
            try { File.Delete(file); } catch (Exception) { }
        }
    }

    [Fact]
    public void ValidateDataRoot_NamesTheUidTheImageActuallyUses()
    {
        var file = Path.Combine(Path.GetTempPath(), "novara-dataroot-file-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("APP_UID");
        try
        {
            File.WriteAllText(file, "not a directory");
            Environment.SetEnvironmentVariable("APP_UID", "4242");

            var problem = SyncHostSetup.ValidateDataRoot(Path.Combine(file, "data"));




            Assert.NotNull(problem);
            if (!OperatingSystem.IsWindows())
                Assert.Contains("4242:4242", problem);
            else
                Assert.Contains("NOVARA_SYNC_DATA", problem);
        }
        finally
        {
            Environment.SetEnvironmentVariable("APP_UID", previous);
            try { File.Delete(file); } catch (Exception) { }
        }
    }

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (Exception) { }
    }



    [Fact]
    public void ValidateWebRoot_RefusesADataDirectoryInsideTheWebRoot()
    {
        var web = Path.Combine(Path.GetTempPath(), "novara-site");
        var problem = SyncHostSetup.ValidateWebRoot(web, Path.Combine(web, "data"));

        Assert.NotNull(problem);
        Assert.Contains("inside the web root", problem);
    }

    [Fact]
    public void ValidateWebRoot_RefusesTheSameDirectoryForBoth()
    {
        var same = Path.Combine(Path.GetTempPath(), "novara-both");
        Assert.NotNull(SyncHostSetup.ValidateWebRoot(same, same));
    }

    [Fact]
    public void ValidateWebRoot_AcceptsAWebRootBelowTheDataDirectory()
    {

        var data = Path.Combine(Path.GetTempPath(), "novara-data");
        Assert.Null(SyncHostSetup.ValidateWebRoot(Path.Combine(data, "site"), data));
    }

    [Fact]
    public void ValidateWebRoot_AcceptsSiblingDirectories_AndTrailingSeparators()
    {
        var root = Path.Combine(Path.GetTempPath(), "novara-root");
        Assert.Null(SyncHostSetup.ValidateWebRoot(Path.Combine(root, "site"), Path.Combine(root, "data")));
        Assert.NotNull(SyncHostSetup.ValidateWebRoot(
            Path.Combine(root, "site") + Path.DirectorySeparatorChar,
            Path.Combine(root, "site", "data")));
    }



    [Fact]
    public void ValidateWebRoot_RefusesASiteTreeContainingAReparsePoint()
    {



        var root = Path.Combine(Path.GetTempPath(), "novara-reparse-" + Guid.NewGuid().ToString("N"));
        var site = Path.Combine(root, "site");
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(site);
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "novara-sync.db"), "ciphertext");

        var link = Path.Combine(site, "pub");
        try
        {

            Assert.Null(SyncHostSetup.ValidateWebRoot(site, data));



            Assert.True(LinkDirectory(link, data),
                "this machine could not create a directory link (symlink or junction) - the check below is untested here");

            var problem = SyncHostSetup.ValidateWebRoot(site, data);

            Assert.NotNull(problem);
            Assert.Contains("link", problem);
            Assert.Contains("pub", problem);
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }






    private static bool LinkDirectory(string linkPath, string targetPath)
    {









        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            if (Directory.Exists(linkPath)) return true;
        }
        catch (Exception) { }

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            process.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch (Exception) { return false; }
    }



    [Fact]
    public async Task KestrelCeiling_SitsJustAboveTheApplicationPayloadLimit()
    {
        var options = new SyncServerOptions { MaxPayloadBytes = 4096 };
        await using var server = await HostedSyncServer.StartAsync(options);

        var kestrel = server.App.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        Assert.Equal(4096 + SyncHostSetup.BodyCeilingGraceBytes, kestrel.Limits.MaxRequestBodySize);
    }



    [Fact]
    public async Task ApiResponses_CarryNoStoreAndTheSecurityHeaders()
    {
        await using var server = await HostedSyncServer.StartAsync();
        using var http = new HttpClient();



        using var response = await http.GetAsync(server.BaseUrl + "/api/v1/space/info");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("noindex, nofollow, noarchive", Header(response, "X-Robots-Tag"));
        Assert.Equal(SyncHostSetup.ContentSecurityPolicy, Header(response, "Content-Security-Policy"));
    }



    [Fact]
    public async Task PayloadTooLarge_CarriesTheLimitInTheBody()
    {
        var options = new SyncServerOptions { MaxPayloadBytes = 4096 };
        await using var server = await HostedSyncServer.StartAsync(options);
        var (client, _, _, _, _) = await server.CreateSpaceAsync();



        var result = await client.PutDataAsync(new string('x', 8192), baseVersion: 0);

        Assert.False(result.Success);
        Assert.Equal(SyncErrorCode.PayloadTooLarge, result.Error);
        Assert.Equal(4096, result.LimitBytes);
        Assert.Null(result.UsedBytes);
    }

    [Fact]
    public async Task QuotaExceeded_CarriesTheLimitAndTheUsageInTheBody()
    {
        var options = new SyncServerOptions { DefaultQuotaBytes = 4096 };
        await using var server = await HostedSyncServer.StartAsync(options);
        var (client, spaceId, spaceKey, deviceId, _) = await server.CreateSpaceAsync();

        Assert.True((await client.PutDataAsync(SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0)).Success);
        Assert.True((await client.PutDataAsync(SealedEnvelope(spaceKey, spaceId, deviceId, 1), 1)).Success);


        var bulky = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(16 * 1024));
        var pushed = await client.PutDataAsync(SealedEnvelope(spaceKey, spaceId, deviceId, 2, bulky), 2);

        Assert.False(pushed.Success);
        Assert.Equal(SyncErrorCode.QuotaExceeded, pushed.Error);
        Assert.Equal(4096, pushed.LimitBytes);
        Assert.NotNull(pushed.UsedBytes);
        Assert.True(pushed.UsedBytes > pushed.LimitBytes, $"used={pushed.UsedBytes} limit={pushed.LimitBytes}");
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string SealedEnvelope(string spaceKey, string spaceId, string deviceId, long baseVersion,
        string payload = "{\"k\":\"v\"}")
    {
        var container = SyncContainer.Seal(Encoding.UTF8.GetBytes(payload), spaceKey, SyncContainer.NewVersionSalt());
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
}
