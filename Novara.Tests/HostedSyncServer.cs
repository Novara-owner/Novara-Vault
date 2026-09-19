using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Novara.Server;
using Novara.Services;
using Novara.Sync.Server;
using Novara.Sync.Server.Storage;
using Xunit;

namespace Novara.Tests;


public sealed class HostedSyncServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private HostedSyncServer(WebApplication app, string storageDirectory, SpaceService service,
        SyncServerOptions options, string baseUrl)
    {
        _app = app;
        StorageDirectory = storageDirectory;
        Service = service;
        Options = options;
        BaseUrl = baseUrl;
    }

    public string StorageDirectory { get; }
    public SpaceService Service { get; }
    public SyncServerOptions Options { get; }
    public string BaseUrl { get; }


    public WebApplication App => _app;







    public static async Task<HostedSyncServer> StartAsync(SyncServerOptions? options = null,
        string? allowedHosts = null, string? trustedProxies = null)
    {
        var storageDirectory = Path.Combine(Path.GetTempPath(), "novara-client-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageDirectory);

        options ??= new SyncServerOptions();
        options.StorageRoot = storageDirectory;
        var service = new SpaceService(new SqliteSpaceStore(storageDirectory), options);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{FreePort()}");
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(service);







        SyncHostSetup.ConfigureHost(builder, options, allowedHosts);

        var app = builder.Build();
        SyncHostSetup.UseHostPipeline(app, webRoot: null, trustedProxies);
        app.MapSyncEndpoints();
        await app.StartAsync();

        return new HostedSyncServer(app, storageDirectory, service, options, app.Urls.First());
    }





    public async Task<(SyncApiClient Client, string SpaceId, string SpaceKey, string DeviceId, string EnrollmentSecret)>
        CreateSpaceAsync(string name = "test-space", string deviceName = "pc")
    {
        var created = Service.CreateSpace(name);
        var spaceId = created.Value!.SpaceId;

        var (client, deviceId, _) = await PairDeviceAsync(spaceId, created.Value.EnrollmentSecret, deviceName);

        return (client, spaceId, SyncKeyWrap.CreateSpaceKey(), deviceId, created.Value.EnrollmentSecret);
    }


    public async Task<(SyncApiClient Client, string DeviceId, string Token)> PairDeviceAsync(
        string spaceId, string enrollmentSecret, string deviceName)
    {
        using var pairing = new SyncApiClient(BaseUrl, spaceId);
        var registered = await pairing.RegisterDeviceAsync(enrollmentSecret, deviceName);
        Assert.True(registered.Success, registered.Message);

        return (new SyncApiClient(BaseUrl, spaceId, registered.Value!.DeviceId, registered.Value.DeviceToken),
            registered.Value.DeviceId, registered.Value.DeviceToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        try { Directory.Delete(StorageDirectory, true); } catch {  }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
