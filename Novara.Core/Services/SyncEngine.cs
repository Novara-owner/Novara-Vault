using Novara.Models;
using System.Text.Json;

namespace Novara.Services;


public enum SyncRoundStatus
{

    Disabled,


    WaitUnlocked,


    RestorePending,


    NoOp,


    Push,


    Pull,






    AutoRebased,


    Conflict,


    RollbackRejected,


    TransportFailure,


    Error,
}









public sealed record SyncRoundOutcome(
    SyncRoundStatus Status,
    string Message = "",
    long LocalVersion = 0,
    long RemoteVersion = 0,
    SyncErrorCode? ProtocolError = null,
    bool Busy = false)
{


    public bool Ok => !Busy && Status is SyncRoundStatus.NoOp or SyncRoundStatus.Push or SyncRoundStatus.Pull or SyncRoundStatus.AutoRebased;


    public bool Changed => Status is SyncRoundStatus.Push or SyncRoundStatus.Pull or SyncRoundStatus.AutoRebased;


    public bool NeedsDecision => Status is SyncRoundStatus.Conflict or SyncRoundStatus.RollbackRejected;
}






public static class SyncEngine
{








    public static async Task<SyncRoundOutcome> RunRoundAsync(
        NovaraStore store,
        SyncState state,
        string spaceKeyBase64,
        SyncApiClient api,
        DateTime utcNow,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(api);

        if (!state.Enabled) return new SyncRoundOutcome(SyncRoundStatus.Disabled, "sync is off");
        if (!SyncKeyWrap.IsWellFormedSpaceKey(spaceKeyBase64))
            return new SyncRoundOutcome(SyncRoundStatus.WaitUnlocked, "the space key is not available yet");
        if (!store.IsLoaded) return new SyncRoundOutcome(SyncRoundStatus.WaitUnlocked, "the vault is locked");
        if (store.IsSaveSuppressed)
            return new SyncRoundOutcome(SyncRoundStatus.RestorePending, "a restore is pending - restart first");
        if (!store.IsEncrypted)
            return new SyncRoundOutcome(SyncRoundStatus.Error, "sync requires an encrypted vault");

        try
        {

            if (!SyncPayloadApplier.TryBuildStablePayloadJson(store.Database, out var localPayload))
                return new SyncRoundOutcome(SyncRoundStatus.Error, "the local database kept changing - the next round will retry");
            var localSha = SyncEnvelopeCodec.HashPayload(localPayload);


            var v5 = await api.SupportsContainerV5Async(ct).ConfigureAwait(false);

            var info = await api.GetInfoAsync(ct).ConfigureAwait(false);
            if (!info.Success) return FromApiFailure(info);

            var remoteVersion = info.Value!.Version;
            var action = SyncPlanner.Decide(new SyncPlanInput(
                Enabled: true,
                Locked: false,
                RestorePending: false,
                RemoteVersion: remoteVersion,
                BaseVersion: state.BaseVersion,
                LastSeenVersion: state.LastSeenVersion,
                LocalSha256: localSha,
                LastPushedSha256: state.LastPushedSha256,
                LocalEmpty: IsEmpty(store.Database),
                RestorePendingConfirm: state.RestorePendingConfirm));

            switch (action)
            {
                case SyncAction.NoOp:
                    return new SyncRoundOutcome(SyncRoundStatus.NoOp, "", state.BaseVersion, remoteVersion);

                case SyncAction.Push:

                    return await PushAsync(store, state, spaceKeyBase64, api, localPayload, localSha,
                        remoteVersion, force: false, utcNow, ct).ConfigureAwait(false);

                case SyncAction.Pull:

                    return await PullAsync(store, state, spaceKeyBase64, api, utcNow, ct).ConfigureAwait(false);

                case SyncAction.Conflict:




                    if (v5 && await RemoteIsManifestAsync(api, ct).ConfigureAwait(false))
                        return await ConflictV5Async(store, state, spaceKeyBase64, api, remoteVersion, utcNow, ct).ConfigureAwait(false);






                    var peek = await SyncConflictResolver.InspectAsync(store, state, spaceKeyBase64, api, ct).ConfigureAwait(false);
                    if (peek.Success && peek.Diff is { IsIdentical: true })
                    {
                        var rebased = await PullAsync(store, state, spaceKeyBase64, api, utcNow, ct).ConfigureAwait(false);
                        if (rebased.Status == SyncRoundStatus.Pull)
                            return rebased with { Status = SyncRoundStatus.AutoRebased,
                                Message = "payloads identical - sha bookkeeping had drifted" };
                        return rebased;
                    }


                    SyncStateUpdater.ObserveVersion(state, remoteVersion);
                    return new SyncRoundOutcome(SyncRoundStatus.Conflict, "", state.BaseVersion, remoteVersion);

                case SyncAction.RollbackRejected:
                    return new SyncRoundOutcome(SyncRoundStatus.RollbackRejected,
                        "the server offered an older version than this device has already seen",
                        state.BaseVersion, remoteVersion);

                default:
                    return new SyncRoundOutcome(SyncRoundStatus.Error, $"unhandled action {action}");
            }
        }
        catch (OperationCanceledException)
        {
            return new SyncRoundOutcome(SyncRoundStatus.TransportFailure, "cancelled");
        }
        catch (Exception e)
        {
            return new SyncRoundOutcome(SyncRoundStatus.Error, e.Message);
        }
    }




    public const int TransportRetryMax = 2;


    public static readonly TimeSpan TransportRetryBaseDelay = TimeSpan.FromSeconds(10);








    public static async Task<SyncRoundOutcome> RunRoundWithTransportRetryAsync(
        NovaraStore store,
        SyncState state,
        string spaceKeyBase64,
        Func<CancellationToken, Task<SyncApiClient>> clientFactory,
        DateTime utcNow,
        CancellationToken ct = default,
        int maxRetries = TransportRetryMax,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= static (span, token) => Task.Delay(span, token);
        try
        {
            var client = await clientFactory(ct).ConfigureAwait(false);
            var outcome = await RunRoundAsync(store, state, spaceKeyBase64, client, utcNow, ct).ConfigureAwait(false);
            for (var attempt = 1;
                 outcome.Status == SyncRoundStatus.TransportFailure && attempt <= maxRetries && !ct.IsCancellationRequested;
                 attempt++)
            {
                await delay(TimeSpan.FromSeconds(TransportRetryBaseDelay.TotalSeconds * attempt), ct).ConfigureAwait(false);
                client = await clientFactory(ct).ConfigureAwait(false);
                outcome = await RunRoundAsync(store, state, spaceKeyBase64, client, utcNow, ct).ConfigureAwait(false);
            }
            return outcome;
        }
        catch (OperationCanceledException)
        {
            return new SyncRoundOutcome(SyncRoundStatus.TransportFailure, "cancelled");
        }
    }



    internal static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public sealed record SyncManifestV5(int Format, Dictionary<string, string> Partitions);





    internal static bool IsWellFormedPid(string? pidHex)
        => pidHex is { Length: 64 } && pidHex.All(char.IsAsciiHexDigit);





    internal static bool ManifestHasAllPartitions(SyncManifestV5 manifest)
        => manifest.Partitions is { } partitions && SyncPartitions.Keys.All(partitions.ContainsKey);








    private static async Task<SyncRoundOutcome> PushV5Core(NovaraStore store, SyncState state, string spaceKeyBase64,
        SyncApiClient api, byte[] localPayload, string localSha, long remoteVersion, bool force, long baseVersion,
        DateTime utcNow, CancellationToken ct)
    {
        var partitions = SyncPartitions.ExtractPartitions(localPayload);
        var pids = partitions.ToDictionary(k => k.Key, k => SyncPartitions.PidOf(k.Value), StringComparer.Ordinal);

        foreach (var (key, pidHex) in pids)
        {



            if (state.ServerManifestPartitions is { } knownPartitions &&
                knownPartitions.TryGetValue(key, out var known) &&
                string.Equals(known, pidHex, StringComparison.OrdinalIgnoreCase)) continue;
            var pidBytes = Convert.FromHexString(pidHex);
            var blob = SyncContainer.SealChunk(partitions[key], spaceKeyBase64, pidBytes);
            var put = await api.PutChunkAsync(pidHex, blob, ct).ConfigureAwait(false);
            if (!put.Success) return FromApiFailure(put);
        }

        var manifest = new SyncManifestV5(5, pids);
        var manifestJson = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestOptions);
        var salt = SyncContainer.NewVersionSalt();
        var manifestContainer = SyncContainer.SealManifest(manifestJson, spaceKeyBase64, salt);
        var envelope = new SyncEnvelope
        {
            Space = state.SpaceId,
            Crypto = SyncContainer.FileVersionManifest,
            Version = baseVersion + 1,
            Base = baseVersion,
            Device = state.DeviceId,
            Ts = SyncEnvelopeCodec.UtcStamp(utcNow),
            Payload = Convert.ToBase64String(manifestContainer),
        };
        SyncEnvelopeCodec.Seal(envelope, spaceKeyBase64);

        var putData = await api.PutDataAsync(SyncEnvelopeCodec.Serialize(envelope), remoteVersion, force, ct,
            chunkIds: pids.Values.ToList()).ConfigureAwait(false);
        if (putData.Success)
        {
            SyncStateUpdater.AfterPush(state, putData.Value, localSha, utcNow);
            state.ServerManifestPartitions = new Dictionary<string, string>(pids, StringComparer.Ordinal);
            return new SyncRoundOutcome(SyncRoundStatus.Push, "", state.BaseVersion, putData.Value);
        }
        if (putData.Error == SyncErrorCode.VersionConflict)
        {
            SyncStateUpdater.ObserveVersion(state, putData.CurrentVersion ?? remoteVersion);
            return new SyncRoundOutcome(SyncRoundStatus.Conflict, putData.Message,
                state.BaseVersion, putData.CurrentVersion ?? remoteVersion);
        }




        if (putData.Error == SyncErrorCode.BadRequest
            && putData.Message?.Contains("chunk", StringComparison.OrdinalIgnoreCase) == true
            && state.ServerManifestPartitions is { Count: > 0 })
        {
            state.ServerManifestPartitions = new Dictionary<string, string>(StringComparer.Ordinal);
            return await PushV5Core(store, state, spaceKeyBase64, api, localPayload, localSha,
                remoteVersion, force, baseVersion, utcNow, ct).ConfigureAwait(false);
        }
        return FromApiFailure(putData);
    }



    private static async Task<SyncRoundOutcome> ConflictV5Async(NovaraStore store, SyncState state, string spaceKeyBase64,
        SyncApiClient api, long remoteVersion, DateTime utcNow, CancellationToken ct)
    {
        var localStable = StableJson.SerializeToUtf8BytesStable(
            SyncFieldPolicy.CloneForSync(store.Database), SyncPayloadApplier.SyncJsonOptions)
            ?? throw new InvalidOperationException("the local database kept changing");
        var localPids = SyncPartitions.ExtractPartitions(localStable);

        var download = await api.GetDataAsync(0, ct).ConfigureAwait(false);
        if (!download.Success) return FromApiFailure(download);

        byte[] container;
        try
        {
            var conflictEnvelope = SyncEnvelopeCodec.Deserialize(download.Value!.Json);


            if (!SyncEnvelopeCodec.Verify(conflictEnvelope, spaceKeyBase64))
                return new SyncRoundOutcome(SyncRoundStatus.Error, "the downloaded envelope failed verification",
                    state.BaseVersion, download.Value!.Version);
            container = SyncEnvelopeCodec.DecodePayload(conflictEnvelope);
        }
        catch (InvalidDataException e)
        {
            return new SyncRoundOutcome(SyncRoundStatus.Error, e.Message, state.BaseVersion, download.Value!.Version);
        }
        var manifestJson = SyncContainer.OpenManifest(container, spaceKeyBase64);
        var manifest = JsonSerializer.Deserialize<SyncManifestV5>(manifestJson, ManifestOptions);
        if (manifest is null || manifest.Format != 5)
            return new SyncRoundOutcome(SyncRoundStatus.Error, "unsupported sync manifest format");


        if (!ManifestHasAllPartitions(manifest))
            return new SyncRoundOutcome(SyncRoundStatus.Error, "the sync manifest is missing a partition");

        var identical = manifest.Partitions.All(kvp =>
            localPids.TryGetValue(kvp.Key, out var localBytes) &&
            SyncPartitions.PidOf(localBytes).Equals(kvp.Value, StringComparison.OrdinalIgnoreCase));
        if (identical)
        {


            SyncStateUpdater.AfterPull(state, remoteVersion,
                SyncEnvelopeCodec.HashPayload(SyncPayloadApplier.BuildPayloadJson(store.Database)), utcNow);
            state.ServerManifestPartitions = new Dictionary<string, string>(manifest.Partitions, StringComparer.Ordinal);
            return new SyncRoundOutcome(SyncRoundStatus.AutoRebased,
                "partition pids identical - payloads match", state.BaseVersion, remoteVersion);
        }
        SyncStateUpdater.ObserveVersion(state, remoteVersion);
        return new SyncRoundOutcome(SyncRoundStatus.Conflict, "", state.BaseVersion, remoteVersion);
    }








    private static async Task<bool> RemoteIsManifestAsync(SyncApiClient api, CancellationToken ct)
    {
        var download = await api.GetDataAsync(0, ct).ConfigureAwait(false);
        if (!download.Success) return false;
        try
        {
            var container = SyncEnvelopeCodec.DecodePayload(SyncEnvelopeCodec.Deserialize(download.Value!.Json));
            return SyncContainer.TryReadHeader(container, out var ver, out _, out _)
                && ver == SyncContainer.FileVersionManifest;
        }
        catch (InvalidDataException) { return false; }
    }







    public static async Task<SyncRoundOutcome> ForcePushAsync(
        NovaraStore store,
        SyncState state,
        string spaceKeyBase64,
        SyncApiClient api,
        DateTime utcNow,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(api);

        if (!SyncKeyWrap.IsWellFormedSpaceKey(spaceKeyBase64))
            return new SyncRoundOutcome(SyncRoundStatus.WaitUnlocked, "the space key is not available yet");
        if (!store.IsLoaded) return new SyncRoundOutcome(SyncRoundStatus.WaitUnlocked, "the vault is locked");
        if (store.IsSaveSuppressed)
            return new SyncRoundOutcome(SyncRoundStatus.RestorePending, "a restore is pending - restart first");
        if (!store.IsEncrypted)
            return new SyncRoundOutcome(SyncRoundStatus.Error, "sync requires an encrypted vault");

        try
        {
            var info = await api.GetInfoAsync(ct).ConfigureAwait(false);
            if (!info.Success) return FromApiFailure(info);








            if (!SyncPayloadApplier.TryBuildStablePayloadJson(store.Database, out var payload))
                return new SyncRoundOutcome(SyncRoundStatus.Error, "the local database kept changing - the next round will retry");

            return await PushAsync(store, state, spaceKeyBase64, api, payload,
                SyncEnvelopeCodec.HashPayload(payload), info.Value!.Version, force: true, utcNow, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new SyncRoundOutcome(SyncRoundStatus.TransportFailure, "cancelled");
        }
        catch (Exception e)
        {
            return new SyncRoundOutcome(SyncRoundStatus.Error, e.Message);
        }
    }


    public static async Task<SyncRoundOutcome> PullAsync(
        NovaraStore store,
        SyncState state,
        string spaceKeyBase64,
        SyncApiClient api,
        DateTime utcNow,
        CancellationToken ct = default)
    {
        var download = await api.GetDataAsync(0, ct).ConfigureAwait(false);
        if (!download.Success) return FromApiFailure(download);



        if (download.Value!.Version < state.LastSeenVersion)
            return new SyncRoundOutcome(SyncRoundStatus.RollbackRejected,
                "the downloaded version is older than this device has already seen",
                state.BaseVersion, download.Value.Version);




        byte[] pullContainer;
        try
        {
            var pullEnvelope = SyncEnvelopeCodec.Deserialize(download.Value!.Json);
            if (!SyncEnvelopeCodec.Verify(pullEnvelope, spaceKeyBase64))
                return new SyncRoundOutcome(SyncRoundStatus.Error, "the downloaded envelope failed verification",
                    state.BaseVersion, download.Value.Version);
            pullContainer = SyncEnvelopeCodec.DecodePayload(pullEnvelope);
        }
        catch (InvalidDataException e)
        {
            return new SyncRoundOutcome(SyncRoundStatus.Error, e.Message, state.BaseVersion, download.Value.Version);
        }
        if (SyncContainer.TryReadHeader(pullContainer, out var pullContainerVer, out _, out _)
            && pullContainerVer == SyncContainer.FileVersionManifest)
        {

            var manifestJson = SyncContainer.OpenManifest(pullContainer, spaceKeyBase64);


            SyncManifestV5? manifest;
            try { manifest = JsonSerializer.Deserialize<SyncManifestV5>(manifestJson, ManifestOptions); }
            catch (JsonException)
            {
                return new SyncRoundOutcome(SyncRoundStatus.Error, "malformed manifest",
                    state.BaseVersion, download.Value.Version);
            }
            if (manifest is null || manifest.Format != 5)
                return new SyncRoundOutcome(SyncRoundStatus.Error, "unsupported sync manifest format");


            if (!ManifestHasAllPartitions(manifest))
                return new SyncRoundOutcome(SyncRoundStatus.Error, "the sync manifest is missing a partition",
                    state.BaseVersion, download.Value.Version);

            var localStable = StableJson.SerializeToUtf8BytesStable(
                SyncFieldPolicy.CloneForSync(store.Database), SyncPayloadApplier.SyncJsonOptions)
                ?? throw new InvalidOperationException("the local database kept changing");
            var localPids = SyncPartitions.ExtractPartitions(localStable);

            var applied = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var (key, pidHex) in manifest.Partitions)
            {
                if (!SyncPartitions.Keys.Contains(key))
                    return new SyncRoundOutcome(SyncRoundStatus.Error, $"unknown partition in manifest: {key}");

                if (!IsWellFormedPid(pidHex))
                    return new SyncRoundOutcome(SyncRoundStatus.Error, "malformed manifest",
                        state.BaseVersion, download.Value.Version);
                var pidBytes = Convert.FromHexString(pidHex);
                var localJson = localPids.TryGetValue(key, out var localBytes) ? localBytes : Array.Empty<byte>();
                if (SyncPartitions.PidOf(localJson).Equals(pidHex, StringComparison.OrdinalIgnoreCase)) continue;
                var blob = await api.GetChunkAsync(pidHex, ct).ConfigureAwait(false);
                if (!blob.Success) return FromApiFailure(blob);
                applied[key] = SyncContainer.OpenChunk(blob.Value!, spaceKeyBase64, pidBytes);
            }






            var db = store.Database;
            var partitionsBefore = (db.MemoGroups, db.MemoEntries, db.PathBackupItems,
                db.TodoCards, db.NoteCards, db.DiaryItems);
            var settingsBefore = (db.AppSettings.AppLanguage, db.AppSettings.Theme, db.AppSettings.Workspaces,
                db.AppSettings.UngroupedCardExpanded);
            void RestoreInMemory()
            {
                db.MemoGroups = partitionsBefore.MemoGroups;
                db.MemoEntries = partitionsBefore.MemoEntries;
                db.PathBackupItems = partitionsBefore.PathBackupItems;
                db.TodoCards = partitionsBefore.TodoCards;
                db.NoteCards = partitionsBefore.NoteCards;
                db.DiaryItems = partitionsBefore.DiaryItems;
                db.AppSettings.AppLanguage = settingsBefore.AppLanguage;
                db.AppSettings.Theme = settingsBefore.Theme;
                db.AppSettings.Workspaces = settingsBefore.Workspaces;
                db.AppSettings.UngroupedCardExpanded = settingsBefore.UngroupedCardExpanded;
            }

            SyncPartitions.ApplyPartitions(db, applied);





            if (store.IsSaveSuppressed)
            {
                RestoreInMemory();
                return new SyncRoundOutcome(SyncRoundStatus.Error, NovaraStore.RestorePendingMessage,
                    state.BaseVersion, download.Value.Version);
            }



            if (!store.SaveSync())
            {
                RestoreInMemory();
                return new SyncRoundOutcome(SyncRoundStatus.Error, "the pulled partitions did not reach disk",
                    state.BaseVersion, download.Value.Version);
            }


            state.ServerManifestPartitions = new Dictionary<string, string>(manifest.Partitions, StringComparer.Ordinal);
        }
        else
        {

            var applied = SyncPayloadApplier.ApplyEnvelope(store, download.Value.Json, spaceKeyBase64);
            if (!applied.Success)
                return new SyncRoundOutcome(SyncRoundStatus.Error, applied.Message, state.BaseVersion, download.Value.Version);
        }




        var appliedSha = SyncEnvelopeCodec.HashPayload(SyncPayloadApplier.BuildPayloadJson(store.Database));
        SyncStateUpdater.AfterPull(state, download.Value.Version, appliedSha, utcNow);
        return new SyncRoundOutcome(SyncRoundStatus.Pull, "", state.BaseVersion, download.Value.Version);
    }

    private static async Task<SyncRoundOutcome> PushAsync(
        NovaraStore store,
        SyncState state,
        string spaceKeyBase64,
        SyncApiClient api,
        byte[] payload,
        string payloadSha256,
        long remoteVersion,
        bool force,
        DateTime utcNow,
        CancellationToken ct)
    {
        var baseVersion = force ? remoteVersion : state.BaseVersion;

        if (await api.SupportsContainerV5Async(ct).ConfigureAwait(false))
            return await PushV5Core(store, state, spaceKeyBase64, api, payload, payloadSha256,
                remoteVersion, force, baseVersion, utcNow, ct).ConfigureAwait(false);
        var envelope = new SyncEnvelope
        {
            Space = state.SpaceId,
            Version = baseVersion + 1,
            Base = baseVersion,
            Device = state.DeviceId,
            Ts = SyncEnvelopeCodec.UtcStamp(utcNow),
            Payload = Convert.ToBase64String(SyncContainer.Seal(payload, spaceKeyBase64, SyncContainer.NewVersionSalt())),
        };
        SyncEnvelopeCodec.Seal(envelope, spaceKeyBase64);

        var put = await api.PutDataAsync(SyncEnvelopeCodec.Serialize(envelope), baseVersion, force, ct)
            .ConfigureAwait(false);

        if (put.Success)
        {
            SyncStateUpdater.AfterPush(state, put.Value, payloadSha256, utcNow);
            return new SyncRoundOutcome(SyncRoundStatus.Push, "", state.BaseVersion, put.Value);
        }

        if (put.Error == SyncErrorCode.VersionConflict)
        {
            SyncStateUpdater.ObserveVersion(state, put.CurrentVersion ?? remoteVersion);
            return new SyncRoundOutcome(SyncRoundStatus.Conflict, put.Message,
                state.BaseVersion, put.CurrentVersion ?? remoteVersion);
        }

        return FromApiFailure(put);
    }

    private static SyncRoundOutcome FromApiFailure<T>(SyncApiResult<T> result)
        => result.TransportFailure
            ? new SyncRoundOutcome(SyncRoundStatus.TransportFailure, result.Message)
            : new SyncRoundOutcome(SyncRoundStatus.Error, result.Message, 0, result.CurrentVersion ?? 0, result.Error);





    private static bool IsEmpty(NovaraDatabase db)
        => db.MemoEntries.All(e => e.IsDeleted)
           && db.DiaryItems.All(e => e.IsDeleted)
           && db.TodoCards.All(e => e.IsDeleted)
           && db.NoteCards.All(e => e.IsDeleted)
           && db.PathBackupItems.All(e => e.IsDeleted)
           && db.MemoGroups.Count == 0;
}
