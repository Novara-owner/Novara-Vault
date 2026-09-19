using Novara.Server;
using Novara.Sync.Server;
using Novara.Sync.Server.Storage;

var options = new SyncServerOptions
{
    StorageRoot = Environment.GetEnvironmentVariable("NOVARA_SYNC_DATA") ?? "data",
    DefaultQuotaBytes = ReadLong("NOVARA_SYNC_QUOTA_BYTES", 500L * 1024 * 1024),
    DefaultMaxVersions = (int)ReadLong("NOVARA_SYNC_MAX_VERSIONS", 10),
    MaxPayloadBytes = ReadLong("NOVARA_SYNC_MAX_PAYLOAD_BYTES", 64L * 1024 * 1024),
    MaxAuthFailures = (int)ReadLong("NOVARA_SYNC_MAX_AUTH_FAILURES", 10),
};



var storeKind = (Environment.GetEnvironmentVariable("NOVARA_SYNC_STORE") ?? "sqlite").Trim().ToLowerInvariant();
ISpaceStore store = storeKind is "file" or "json"
    ? new FileSpaceStore(options.StorageRoot)
    : new SqliteSpaceStore(options.StorageRoot);
var service = new SpaceService(store, options);


if (args.Length > 0 && args[0].Equals("space", StringComparison.OrdinalIgnoreCase))
    return Cli.RunSpaceCommand(service, args);





var webRoot = Environment.GetEnvironmentVariable("NOVARA_SYNC_WEB_ROOT")
    ?? Path.Combine(AppContext.BaseDirectory, "Novara.Web");
if (!Directory.Exists(webRoot))
{
    webRoot = null;
}
else if (SyncHostSetup.ValidateWebRoot(webRoot, options.StorageRoot) is string webProblem)
{
    Console.Error.WriteLine($"NovaraSync: refusing to start - {webProblem}.");
    Console.Error.WriteLine("Set NOVARA_SYNC_WEB_ROOT to the site directory alone, or move the data directory out of it.");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(service);




SyncHostSetup.ConfigureHost(builder, options, Environment.GetEnvironmentVariable("NOVARA_SYNC_ALLOWED_HOSTS"),
    warning => Console.Error.WriteLine($"NovaraSync: {warning}"));

var app = builder.Build();

SyncHostSetup.UseHostPipeline(app, webRoot,
    Environment.GetEnvironmentVariable("NOVARA_SYNC_TRUSTED_PROXIES"),
    entry => Console.Error.WriteLine(
        $"NovaraSync: ignoring NOVARA_SYNC_TRUSTED_PROXIES entry '{entry}' (expected an IP address)"));

app.MapSyncEndpoints();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.Run();
return 0;

static long ReadLong(string name, long fallback)
    => long.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;
