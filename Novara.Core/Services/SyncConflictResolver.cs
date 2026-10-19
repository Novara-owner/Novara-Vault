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

        byte[] container;
        try
        {
            var envelope = SyncEnvelopeCodec.Deserialize(download.Value!.Json);


            if (!SyncEnvelopeCodec.Verify(envelope, spaceKeyBase64))
                return SyncConflictInspection.Fail("the downloaded envelope failed verification");

            container = SyncEnvelopeCodec.DecodePayload(envelope);
        }
        catch (InvalidDataException e) { return SyncConflictInspection.Fail(e.Message); }



        if (SyncContainer.TryReadHeader(container, out var containerVer, out _, out _)
            && containerVer == SyncContainer.FileVersionManifest)
        {
            var (diff, message) = await InspectManifestAsync(store, spaceKeyBase64, api, container, ct).ConfigureAwait(false);
            if (diff is null) return SyncConflictInspection.Fail(message);
            return new SyncConflictInspection(true, "", state.BaseVersion, download.Value!.Version, diff, container);
        }

        NovaraDatabase remoteDb;
        try
        {
            remoteDb = SyncJson.DeserializeDatabase(System.Text.Encoding.UTF8.GetString(
                SyncContainer.Open(container, spaceKeyBase64)));
        }
        catch (InvalidDataException e) { return SyncConflictInspection.Fail(e.Message); }

        SyncDiffSummary v4Diff;
        try { v4Diff = SyncDiff.Compare(store.Database, remoteDb); }
        catch (Exception e) { return SyncConflictInspection.Fail("conflict inspection failed: " + e.Message); }
        return new SyncConflictInspection(true, "", state.BaseVersion, download.Value!.Version, v4Diff, container);
    }








    private static async Task<(SyncDiffSummary? Diff, string Message)> InspectManifestAsync(
        NovaraStore store,
        string spaceKeyBase64,
        SyncApiClient api,
        byte[] manifestContainer,
        CancellationToken ct)
    {
        try
        {
            var manifest = System.Text.Json.JsonSerializer.Deserialize<SyncEngine.SyncManifestV5>(
                SyncContainer.OpenManifest(manifestContainer, spaceKeyBase64), SyncEngine.ManifestOptions);
            if (manifest is null || manifest.Format != 5)
                return (null, "unsupported sync manifest format");


            if (!SyncEngine.ManifestHasAllPartitions(manifest))
                return (null, "the sync manifest is missing a partition");

            var localStable = StableJson.SerializeToUtf8BytesStable(
                SyncFieldPolicy.CloneForSync(store.Database), SyncPayloadApplier.SyncJsonOptions);
            if (localStable is null) return (null, "the local database kept changing");
            var localPids = SyncPartitions.ExtractPartitions(localStable);

            var applied = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var (key, pidHex) in manifest.Partitions)
            {
                if (!SyncPartitions.Keys.Contains(key))
                    return (null, $"unknown partition in manifest: {key}");

                if (!SyncEngine.IsWellFormedPid(pidHex))
                    return (null, "malformed manifest");
                var pidBytes = Convert.FromHexString(pidHex);
                var localJson = localPids.TryGetValue(key, out var localBytes) ? localBytes : Array.Empty<byte>();
                if (SyncPartitions.PidOf(localJson).Equals(pidHex, StringComparison.OrdinalIgnoreCase))
                {

                    applied[key] = localJson;
                    continue;
                }
                var blob = await api.GetChunkAsync(pidHex, ct).ConfigureAwait(false);
                if (!blob.Success) return (null, blob.Message);
                applied[key] = SyncContainer.OpenChunk(blob.Value!, spaceKeyBase64, pidBytes);
            }

            var remoteDb = new NovaraDatabase();
            SyncPartitions.ApplyPartitions(remoteDb, applied);
            return (SyncDiff.Compare(store.Database, remoteDb), "");
        }
        catch (OperationCanceledException) { return (null, "cancelled"); }
        catch (InvalidDataException e) { return (null, e.Message); }
        catch (FormatException) { return (null, "malformed manifest"); }
        catch (ArgumentNullException) { return (null, "malformed manifest"); }
        catch (System.Text.Json.JsonException) { return (null, "malformed manifest"); }
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



            if (!SyncPayloadApplier.TryBuildStablePayloadJson(store.Database, out var payload))
                return SyncExportOutcome.Fail("the vault kept changing during export - try again");
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
