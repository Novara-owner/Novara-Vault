using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Novara.Models;
using Novara.Sync.Server;

namespace Novara.Server;





public static class SyncHostSetup
{





    public const long BodyCeilingGraceBytes = 64 * 1024;







    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self' data:; connect-src 'self'; manifest-src 'self'; " +
        "worker-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";

    private static readonly JsonSerializerOptions ErrorJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };








    public static void ConfigureKestrel(WebApplicationBuilder builder, SyncServerOptions options)
    {
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = options.MaxPayloadBytes + BodyCeilingGraceBytes;
        });
    }








    public static void ConfigureAllowedHosts(WebApplicationBuilder builder, string allowedHosts,
        Action<string>? warn = null)
    {
        var hosts = NormalizeAllowedHosts(allowedHosts, warn);
        builder.Services.Configure<HostFilteringOptions>(filtering =>
        {
            filtering.AllowedHosts = hosts;

            filtering.AllowEmptyHosts = false;
        });
    }














    public static string[] NormalizeAllowedHosts(string allowedHosts, Action<string>? warn = null)
    {
        var hosts = allowedHosts
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in hosts)
        {
            if (entry.EndsWith(".", StringComparison.Ordinal))
                warn?.Invoke($"{entry} ends with a dot - AllowedHosts entries are hostnames, "
                    + "and a trailing dot never matches a request Host");

            else if (!entry.StartsWith('[') && entry.Contains(':'))
                warn?.Invoke($"{entry} carries a port - AllowedHosts entries are hostnames, "
                    + "and an entry with a port never matches a request Host");
        }

        return hosts;
    }





    public static ForwardedHeadersOptions BuildForwardedHeaders(string? trustedProxies, Action<string>? warn = null)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost,
        };

        foreach (var entry in (trustedProxies ?? "")
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {




            if (IPAddress.TryParse(entry, out var proxyAddress)) options.KnownProxies.Add(proxyAddress);
            else warn?.Invoke(entry);
        }

        return options;
    }







    public static void ConfigureHost(WebApplicationBuilder builder, SyncServerOptions options, string? allowedHosts,
        Action<string>? warn = null)
    {
        ConfigureKestrel(builder, options);


        if (!string.IsNullOrWhiteSpace(allowedHosts)) ConfigureAllowedHosts(builder, allowedHosts!, warn);
    }





    public static void UseHostPipeline(WebApplication app, string? webRoot, string? trustedProxies,
        Action<string>? warn = null)
    {





        app.UseForwardedHeaders(BuildForwardedHeaders(trustedProxies, warn));
        UseSyncPipeline(app, webRoot);
    }





    public static void UseSyncPipeline(WebApplication app, string? webRoot)
    {

        app.Use(async (context, next) =>
        {
            ApplySecurityHeaders(context.Response);
            await next();
        });





        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(SyncApi.ApiPrefix))
                context.Response.Headers.CacheControl = "no-store";
            await next();
        });






        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(SyncApi.ApiPrefix)
                && RequiresSecureTransport(context.Request.IsHttps, context.Connection.RemoteIpAddress))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    new SyncErrorResponse
                    {
                        Error = SyncErrorCodes.ToWire(SyncErrorCode.Forbidden),
                        Message = "https is required for api requests: terminate tls in front of this "
                            + "process and forward x-forwarded-proto (https), then set "
                            + "NOVARA_SYNC_TRUSTED_PROXIES if the proxy runs on another host",
                    },
                    ErrorJson));
                return;
            }

            await next();
        });

        if (webRoot is null) return;







        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals("/web", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/web/", permanent: true);
                return;
            }

            await next();
        });

        var webFiles = new PhysicalFileProvider(webRoot);
        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = webFiles,
            RequestPath = "/web",

            RedirectToAppendTrailingSlash = false,
        });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = webFiles, RequestPath = "/web" });
        app.MapGet("/", () => Results.Redirect("/web/"));
    }







    public static bool RequiresSecureTransport(bool isHttps, IPAddress? remoteAddress)
        => !isHttps && (remoteAddress is null || !IPAddress.IsLoopback(remoteAddress));













    public static string? ValidateDataRoot(string dataRoot)
    {


        var probe = Path.Combine(dataRoot, ".novara-write-probe");
        try
        {
            Directory.CreateDirectory(dataRoot);
            using (var stream = new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.WriteByte(0);
            }
        }
        catch (Exception e)
        {



            if (OperatingSystem.IsWindows())
            {
                return $"the data directory ({dataRoot}) cannot be written "
                    + $"({e.GetType().Name}: {e.Message}). Set the NOVARA_SYNC_DATA environment "
                    + "variable to a writable directory (see the bundled 1-建空间.cmd / 2-启动服务端.cmd scripts)";
            }


            var uid = Environment.GetEnvironmentVariable("APP_UID") ?? "1654";
            return $"the data directory ({dataRoot}) cannot be written "
                + $"({e.GetType().Name}: {e.Message}). The container runs as a non-root user, so a "
                + $"bind-mounted ./data created by the host is owned by another uid - fix it with "
                + $"`sudo chown -R {uid}:{uid} ./data`, or switch to the named volume shown in the "
                + "compose file (see deploy/README.md)";
        }

        try { File.Delete(probe); }
        catch (Exception) {  }

        return null;
    }









    public static string? ValidateWebRoot(string webRoot, string dataRoot)
    {
        var web = NormalizedFullPath(webRoot);
        var data = NormalizedFullPath(dataRoot);
        if (web is null || data is null) return null;

        if (string.Equals(web, data, StringComparison.OrdinalIgnoreCase))
            return $"the web root and the data directory are the same directory ({web})";

        if (data.StartsWith(web + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return $"the data directory ({data}) sits inside the web root ({web}), so /web would serve it";







        if (FirstReparsePoint(web) is string link)
            return $"the web root contains a link ({link}), and /web would serve whatever it points at - "
                + "which this check cannot prove is not the data directory";

        return null;
    }







    private static string? FirstReparsePoint(string root)
    {


        FileAttributes rootAttributes;
        try { rootAttributes = File.GetAttributes(root); }
        catch (Exception) { return null; }
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0) return root;

        var pending = new Queue<string>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();

            IReadOnlyList<string> children;
            try { children = Directory.GetFileSystemEntries(directory); }
            catch (Exception) { continue; }

            foreach (var child in children)
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(child); }
                catch (Exception) { continue; }

                if ((attributes & FileAttributes.ReparsePoint) != 0) return child;
                if ((attributes & FileAttributes.Directory) != 0) pending.Enqueue(child);
            }
        }

        return null;
    }

    private static void ApplySecurityHeaders(HttpResponse response)
    {
        var headers = response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";


        headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        headers["Content-Security-Policy"] = ContentSecurityPolicy;
    }

    private static string? NormalizedFullPath(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception) { return null; }
    }
}
