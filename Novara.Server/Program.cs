using Novara.Server;
using Novara.Sync.Server;
using Novara.Sync.Server.Storage;

var options = new SyncServerOptions
{
    StorageRoot = Environment.GetEnvironmentVariable("NOVARA_SYNC_DATA") ?? "data",
    DefaultQuotaBytes = ReadLong("NOVARA_SYNC_QUOTA_BYTES", 500L * 1024 * 1024),
    DefaultMaxVersions = ReadInt("NOVARA_SYNC_MAX_VERSIONS", 10),
    MaxPayloadBytes = ReadLong("NOVARA_SYNC_MAX_PAYLOAD_BYTES", 64L * 1024 * 1024),
    MaxAuthFailures = ReadInt("NOVARA_SYNC_MAX_AUTH_FAILURES", 10),
};






if (SyncHostSetup.ValidateDataRoot(options.StorageRoot) is string dataProblem)
{
    Console.Error.WriteLine($"NovaraSync: refusing to start - {dataProblem}.");
    return 1;
}






var storeSetting = Environment.GetEnvironmentVariable("NOVARA_SYNC_STORE");
var storeKind = string.IsNullOrWhiteSpace(storeSetting) ? "sqlite" : storeSetting.Trim().ToLowerInvariant();
if (storeKind is not ("sqlite" or "file" or "json"))
{
    Console.Error.WriteLine($"NovaraSync: unknown NOVARA_SYNC_STORE '{storeSetting}' - expected 'sqlite' or 'file'.");
    return 1;
}
ISpaceStore store;
try
{
    store = storeKind is "file" or "json"
        ? new FileSpaceStore(options.StorageRoot)
        : new SqliteSpaceStore(options.StorageRoot);
}
catch (Exception ex)
{



    Console.Error.WriteLine($"storage backend failed to initialize ({storeKind}): {ex.Message}");
    return 1;
}
var service = new SpaceService(store, options);


if (args.Length > 0 && args[0].Equals("space", StringComparison.OrdinalIgnoreCase))
    return Cli.RunSpaceCommand(service, args);




if (args.Length > 0 && !args[0].StartsWith('-'))
{
    Console.Error.WriteLine($"unknown command: {args[0]} - expected 'space <create|list|show|delete|rotate-secret>', or no arguments to run the server.");
    return 2;
}





var webRootSetting = Environment.GetEnvironmentVariable("NOVARA_SYNC_WEB_ROOT");
var webRoot = webRootSetting ?? Path.Combine(AppContext.BaseDirectory, "Novara.Web");
if (!Directory.Exists(webRoot))
{



    if (!string.IsNullOrWhiteSpace(webRootSetting))
    {
        Console.Error.WriteLine($"NovaraSync: NOVARA_SYNC_WEB_ROOT points at a directory that does not exist: {webRootSetting}");
        return 1;
    }
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
{
    var raw = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(raw)) return fallback;
    if (long.TryParse(raw, out var value) && value > 0) return value;


    Console.Error.WriteLine($"NovaraSync: ignoring {name}={raw} (expected a positive integer) - using {fallback}.");
    return fallback;
}






static int ReadInt(string name, int fallback)
{
    var raw = Environment.GetEnvironmentVariable(name);
    if (!long.TryParse(raw, out var value) || value <= 0 || value > int.MaxValue)
    {

        if (!string.IsNullOrWhiteSpace(raw))
            Console.Error.WriteLine($"NovaraSync: ignoring {name}={raw} (expected 1..{int.MaxValue}) - using {fallback}.");
        return fallback;
    }
    return (int)value;
}
