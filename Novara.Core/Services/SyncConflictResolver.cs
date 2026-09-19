using Novara.Models;

namespace Novara.Services;





public sealed record SyncConflictInspection(
    bool Success,
    string Message,
    long LocalVersion,
    long RemoteVersion,
    SyncDiffSummary? Diff,
    byte[] RemoteContainer)
{
    public static SyncConflictInspection Fail(string message)
        => new(false, message, 0, 0, null, Array.Empty<byte>());
}


public sealed record SyncExportOutcome(bool Success, string Message)
{
    public static SyncExportOutcome Ok() => new(true, "");
    public static SyncExportOutcome Fail(string message) => new(false, message);
}












public static class SyncConflictResolver
{









    public static async Task<SyncConflictInspection> InspectAsync(
        NovaraStore store,
        SyncState state,
        string spaceKeyBase64,
        SyncApiClient api,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(api);

        if (!store.IsLoaded) return SyncConflictInspection.Fail("the vault is locked");
        if (!store.IsEncrypted) return SyncConflictInspection.Fail("sync requires an encrypted vault");
        if (store.IsSaveSuppressed) return SyncConflictInspection.Fail("a restore is pending - restart first");
        if (!SyncKeyWrap.IsWellFormedSpaceKey(spaceKeyBase64))
            return SyncConflictInspection.Fail("the space key is not available yet");

        SyncApiResult<RemoteEnvelope> download;
        try { download = await api.GetDataAsync(0, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return SyncConflictInspection.Fail("cancelled"); }
        catch (Exception e) { return SyncConflictInspection.Fail(e.Message); }

        if (!download.Success) return SyncConflictInspection.Fail(download.Message);

        SyncEnvelope envelope;
        byte[] container;
        NovaraDatabase remoteDb;
        try
        {
            envelope = SyncEnvelopeCodec.Deserialize(download.Value!.Json);


            if (!SyncEnvelopeCodec.Verify(envelope, spaceKeyBase64))
                return SyncConflictInspection.Fail("the downloaded envelope failed verification");

            container = SyncEnvelopeCodec.DecodePayload(envelope);
            remoteDb = SyncJson.DeserializeDatabase(System.Text.Encoding.UTF8.GetString(
                SyncContainer.Open(container, spaceKeyBase64)));
        }
        catch (InvalidDataException e) { return SyncConflictInspection.Fail(e.Message); }

        return new SyncConflictInspection(
            true,
            "",
            state.BaseVersion,
            download.Value!.Version,
            SyncDiff.Compare(store.Database, remoteDb),
            container);
    }





    public static SyncExportOutcome ExportRemoteVersion(string path, byte[] remoteContainer)
    {
        if (string.IsNullOrWhiteSpace(path)) return SyncExportOutcome.Fail("a target path is required");
        if (remoteContainer is null || remoteContainer.Length == 0)
            return SyncExportOutcome.Fail("there is no downloaded version to export");

        return WriteAtomically(path, remoteContainer);
    }





    public static SyncExportOutcome ExportLocalVersion(string path, NovaraStore store, string spaceKeyBase64)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrWhiteSpace(path)) return SyncExportOutcome.Fail("a target path is required");
        if (!SyncKeyWrap.IsWellFormedSpaceKey(spaceKeyBase64))
            return SyncExportOutcome.Fail("the space key is not available yet");
        if (!store.IsLoaded) return SyncExportOutcome.Fail("the vault is locked");
        if (store.IsSaveSuppressed) return SyncExportOutcome.Fail("a restore is pending - restart first");

        byte[] container;
        try
        {
            var payload = SyncPayloadApplier.BuildPayloadJson(store.Database);
            container = SyncContainer.Seal(payload, spaceKeyBase64, SyncContainer.NewVersionSalt());
        }
        catch (Exception e) { return SyncExportOutcome.Fail(e.Message); }

        return WriteAtomically(path, container);
    }





    public static string SuggestFileName(bool remote, long version, DateTime stamp)
        => $"novara-sync-{(remote ? "remote" : "local")}-v{Math.Max(version, 0)}-{stamp:yyyyMMdd-HHmmss}.novaenc";


    private static SyncExportOutcome WriteAtomically(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            return SyncExportOutcome.Ok();
        }
        catch (IOException e) { return SyncExportOutcome.Fail(e.Message); }
        catch (UnauthorizedAccessException e) { return SyncExportOutcome.Fail(e.Message); }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch {  }
        }
    }
}
