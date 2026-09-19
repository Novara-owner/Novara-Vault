using Novara.Models;
using Novara.Sync.Server;
using Novara.Sync.Server.Storage;

namespace Novara.Server;


public static class SyncEndpoints
{


    private const string SpaceHeader = SyncApi.SpaceHeader;
    private const string DeviceHeader = SyncApi.DeviceHeader;
    private const string KeyWrapVersionHeader = SyncApi.KeyWrapVersionHeader;
    private const string VersionHeader = SyncApi.VersionHeader;

    private const string ReadPrefix = SyncApi.ReadScheme + " ";

    public static void MapSyncEndpoints(this WebApplication app)
    {
        var api = app.MapGroup(SyncApi.ApiPrefix);

        api.MapPost("/devices/register", (DeviceRegistrationRequest body, SpaceService service, HttpContext ctx) =>
        {
            if (!FileSpaceStore.IsSafeId(body.SpaceId))
                return Error(SyncErrorCode.BadRequest, "space id is not valid");



            var result = service.RegisterDevice(body.SpaceId, body.EnrollmentSecret, body.DeviceName,
                ctx.Connection.RemoteIpAddress?.ToString());
            if (!result.Success) return Map(result);

            return Results.Ok(new DeviceRegistrationResponse
            {
                DeviceId = result.Value!.DeviceId,
                DeviceToken = result.Value.DeviceToken,
            });
        });

        api.MapGet("/space/info", (HttpContext ctx, SpaceService service) =>
        {


            var auth = ResolveDevice(ctx, service);
            return auth.Success ? Results.Ok(service.GetInfo(auth.Value!.Space).Value) : Map(auth);
        });

        api.MapGet("/space/data", (HttpContext ctx, SpaceService service, long? version) =>
        {
            var auth = AuthorizeRead(ctx, service);
            if (!auth.Success) return Map(auth);

            var result = service.GetData(auth.Value!, version ?? 0);
            if (!result.Success) return Map(result);

            ctx.Response.Headers[VersionHeader] = result.Value!.Version.Version
                .ToString(System.Globalization.CultureInfo.InvariantCulture);


            return Results.Content(result.Value.Json, "application/json");
        });




        api.MapPut("/space/data", async (HttpContext ctx, SpaceService service, string? force) =>
        {


            var auth = ResolveDevice(ctx, service);
            if (!auth.Success) return Map(auth);

            if (!TryReadIfMatch(ctx, out var baseVersion))
                return Error(SyncErrorCode.BadRequest, "If-Match header with the base version is required");





            if (ctx.Request.ContentLength is long declaredLength && declaredLength > service.Options.MaxPayloadBytes)
                return Error(SyncErrorCode.PayloadTooLarge, "payload exceeds the server limit",
                    service.Options.MaxPayloadBytes);

            using var reader = new StreamReader(ctx.Request.Body, System.Text.Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body)) return Error(SyncErrorCode.BadRequest, "envelope body is required");

            var result = service.PutData(auth.Value!.Space, auth.Value.Device, body, baseVersion, WantsForce(force));
            return result.Success ? Results.Ok(new { version = result.Value }) : Map(result);
        });

        api.MapGet("/space/versions", (HttpContext ctx, SpaceService service) =>
        {
            var auth = AuthorizeDevice(ctx, service);
            return auth.Success ? Results.Ok(service.ListVersions(auth.Value!.Space).Value) : Map(auth);
        });

        api.MapGet("/space/keywrap", (HttpContext ctx, SpaceService service) =>
        {
            var auth = AuthorizeRead(ctx, service);
            if (!auth.Success) return Map(auth);

            var result = service.GetKeyWrap(auth.Value!);
            if (!result.Success) return Map(result);

            ctx.Response.Headers[KeyWrapVersionHeader] = auth.Value!.KeyWrapVersion
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Content(result.Value!, "application/json");
        });

        api.MapPut("/space/keywrap", async (HttpContext ctx, SpaceService service) =>
        {
            var auth = AuthorizeDevice(ctx, service);
            if (!auth.Success) return Map(auth);

            if (!TryReadIfMatch(ctx, out var ifMatch))
                return Error(SyncErrorCode.BadRequest, "If-Match header with the keywrap version is required");


            if (ctx.Request.ContentLength is long declaredKeyWrapLength && declaredKeyWrapLength > service.Options.MaxPayloadBytes)
                return Error(SyncErrorCode.PayloadTooLarge, "keywrap record exceeds the server limit",
                    service.Options.MaxPayloadBytes);

            using var reader = new StreamReader(ctx.Request.Body, System.Text.Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body)) return Error(SyncErrorCode.BadRequest, "keywrap body is required");

            var result = service.PutKeyWrap(auth.Value!.Space, body, ifMatch);
            return result.Success ? Results.Ok(new { keyWrapVersion = result.Value }) : Map(result);
        });


        api.MapPost("/space/read-token", (HttpContext ctx, SpaceService service) =>
        {
            var auth = AuthorizeDevice(ctx, service);
            if (!auth.Success) return Map(auth);

            var result = service.IssueReadToken(auth.Value!.Space);
            return result.Success
                ? Results.Ok(new ReadTokenResponse { ReadToken = result.Value! })
                : Map(result);
        });

        api.MapDelete("/space/read-token", (HttpContext ctx, SpaceService service) =>
        {
            var auth = AuthorizeDevice(ctx, service);
            if (!auth.Success) return Map(auth);

            var result = service.RevokeReadToken(auth.Value!.Space);
            return result.Success ? Results.NoContent() : Map(result);
        });





        api.MapPost("/space/editor-device", (HttpContext ctx, SpaceService service) =>
        {
            var auth = AuthorizeDevice(ctx, service);
            if (!auth.Success) return Map(auth);

            var result = service.IssueEditorDevice(auth.Value!.Space);
            if (!result.Success) return Map(result);

            return Results.Ok(new DeviceRegistrationResponse
            {
                DeviceId = result.Value!.DeviceId,
                DeviceToken = result.Value.DeviceToken,
            });
        });

        api.MapGet("/devices", (HttpContext ctx, SpaceService service) =>
        {
            var auth = AuthorizeDevice(ctx, service);
            return auth.Success ? Results.Ok(service.ListDevices(auth.Value!.Space.SpaceId).Value) : Map(auth);
        });

        api.MapDelete("/devices/{deviceId}", (string deviceId, HttpContext ctx, SpaceService service) =>
        {
            var auth = AuthorizeDevice(ctx, service);
            if (!auth.Success) return Map(auth);

            var result = service.RevokeDevice(auth.Value!.Space.SpaceId, deviceId);
            return result.Success ? Results.NoContent() : Map(result);
        });

        api.MapPost("/devices/{deviceId}/reset-token", (string deviceId, HttpContext ctx, SpaceService service) =>
        {
            var auth = AuthorizeDevice(ctx, service);
            if (!auth.Success) return Map(auth);

            var result = service.ResetToken(auth.Value!.Space.SpaceId, deviceId);
            if (!result.Success) return Map(result);

            return Results.Ok(new DeviceRegistrationResponse
            {
                DeviceId = result.Value!.DeviceId,
                DeviceToken = result.Value.DeviceToken,
            });
        });
    }






    private sealed record DeviceContext(SpaceRecord Space, DeviceRecord Device);






    private static SyncResult<DeviceContext> ResolveDevice(HttpContext ctx, SpaceService service)
    {



        if (ctx.Request.Headers.Authorization.ToString()
                .StartsWith(ReadPrefix, StringComparison.OrdinalIgnoreCase))
            return SyncResult<DeviceContext>.Fail(SyncErrorCode.Forbidden, "this credential is read-only");

        var spaceId = ctx.Request.Headers[SpaceHeader].ToString();
        var deviceId = ctx.Request.Headers[DeviceHeader].ToString();
        if (!FileSpaceStore.IsSafeId(spaceId))
            return SyncResult<DeviceContext>.Fail(SyncErrorCode.BadRequest, "space header is missing or invalid");
        if (string.IsNullOrEmpty(deviceId))
            return SyncResult<DeviceContext>.Fail(SyncErrorCode.BadRequest, "device header is required");

        var auth = service.Authorize(spaceId, deviceId, ReadBearer(ctx));
        if (!auth.Success) return SyncResult<DeviceContext>.Fail(auth.Error, auth.Message, auth.CurrentVersion);

        var device = service.DeviceOf(spaceId, deviceId);
        return device is null
            ? SyncResult<DeviceContext>.Fail(SyncErrorCode.Unauthenticated, "device token is not valid")
            : SyncResult<DeviceContext>.Ok(new DeviceContext(auth.Value!, device));
    }






    private static SyncResult<DeviceContext> AuthorizeDevice(HttpContext ctx, SpaceService service)
    {
        var resolved = ResolveDevice(ctx, service);
        if (!resolved.Success) return resolved;
        return resolved.Value!.Device.Kind == DeviceKind.Editor
            ? SyncResult<DeviceContext>.Fail(SyncErrorCode.Forbidden, "this credential is data-only")
            : resolved;
    }






    private static SyncResult<SpaceRecord> AuthorizeRead(HttpContext ctx, SpaceService service)
    {
        var spaceId = ctx.Request.Headers[SpaceHeader].ToString();
        if (!FileSpaceStore.IsSafeId(spaceId))
            return SyncResult<SpaceRecord>.Fail(SyncErrorCode.BadRequest, "space header is missing or invalid");

        var header = ctx.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(ReadPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var device = ResolveDevice(ctx, service);
            return device.Success
                ? SyncResult<SpaceRecord>.Ok(device.Value!.Space)
                : SyncResult<SpaceRecord>.Fail(device.Error, device.Message, device.CurrentVersion);
        }



        return service.AuthorizeRead(spaceId, header[ReadPrefix.Length..].Trim(),
            ctx.Connection.RemoteIpAddress?.ToString());
    }

    private static string? ReadBearer(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : null;
    }

    private static bool TryReadIfMatch(HttpContext ctx, out long version)
    {
        version = 0;
        var raw = ctx.Request.Headers.IfMatch.ToString().Trim().Trim('"');
        return !string.IsNullOrEmpty(raw) && long.TryParse(raw, out version) && version >= 0;
    }





    private static bool WantsForce(string? raw)
        => !string.IsNullOrEmpty(raw) && raw != "0" && !raw.Equals("false", StringComparison.OrdinalIgnoreCase);

    private static IResult Map<T>(SyncResult<T> result)
        => Results.Json(
            new SyncErrorResponse
            {
                Error = Code(result.Error),
                Message = result.Message,
                CurrentVersion = result.CurrentVersion,


                LimitBytes = result.LimitBytes,
                UsedBytes = result.UsedBytes,
            },
            statusCode: Status(result.Error));

    private static IResult Error(SyncErrorCode code, string message)
        => Results.Json(new SyncErrorResponse { Error = Code(code), Message = message }, statusCode: Status(code));


    private static IResult Error(SyncErrorCode code, string message, long limitBytes)
        => Results.Json(
            new SyncErrorResponse { Error = Code(code), Message = message, LimitBytes = limitBytes },
            statusCode: Status(code));


    private static int Status(SyncErrorCode code) => SyncErrorCodes.HttpStatus(code);

    private static string Code(SyncErrorCode code) => SyncErrorCodes.ToWire(code);
}
