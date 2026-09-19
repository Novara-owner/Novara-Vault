using Novara.Models;

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
    SyncErrorCode? ProtocolError = null)
{

    public bool Ok => Status is SyncRoundStatus.NoOp or SyncRoundStatus.Push or SyncRoundStatus.Pull or SyncRoundStatus.AutoRebased;


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



            state.BaseVersion = info.Value!.Version;


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

        var applied = SyncPayloadApplier.ApplyEnvelope(store, download.Value.Json, spaceKeyBase64);
        if (!applied.Success)
            return new SyncRoundOutcome(SyncRoundStatus.Error, applied.Message, state.BaseVersion, download.Value.Version);




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
