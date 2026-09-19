using Novara.Models;

namespace Novara.Services;


public enum SyncAction
{

    NoOp,


    Push,


    Pull,


    Conflict,


    WaitUnlocked,


    RestorePending,


    Disabled,


    RollbackRejected,
}


public readonly record struct SyncPlanInput(
    bool Enabled,
    bool Locked,
    bool RestorePending,
    long RemoteVersion,
    long BaseVersion,
    long LastSeenVersion,
    string LocalSha256,
    string LastPushedSha256,
    bool LocalEmpty = false,
    bool RestorePendingConfirm = false);
















public static class SyncPlanner
{
    public static SyncAction Decide(SyncPlanInput input)
    {
        if (!input.Enabled) return SyncAction.Disabled;
        if (input.Locked) return SyncAction.WaitUnlocked;
        if (input.RestorePending) return SyncAction.RestorePending;





        if (input.RestorePendingConfirm) return SyncAction.Conflict;

        var localChanged = !string.Equals(input.LocalSha256, input.LastPushedSha256, StringComparison.OrdinalIgnoreCase);


        if (input.RemoteVersion < input.LastSeenVersion) return SyncAction.RollbackRejected;





        var hasSynced = input.BaseVersion > 0 || input.LastSeenVersion > 0 || !string.IsNullOrEmpty(input.LastPushedSha256);
        if (!hasSynced && input.RemoteVersion > 0)
            return input.LocalEmpty ? SyncAction.Pull : SyncAction.Conflict;

        if (input.RemoteVersion == input.BaseVersion)
            return localChanged ? SyncAction.Push : SyncAction.NoOp;

        if (input.RemoteVersion > input.BaseVersion)
            return localChanged ? SyncAction.Conflict : SyncAction.Pull;


        return SyncAction.RollbackRejected;
    }
}






public static class SyncStateUpdater
{
    public static SyncState AfterPush(SyncState state, long newVersion, string pushedSha256, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.BaseVersion = newVersion;
        state.LastSeenVersion = Math.Max(state.LastSeenVersion, newVersion);
        state.LastPushedSha256 = pushedSha256 ?? "";
        state.LastSyncAt = utcNow;
        return state;
    }


    public static SyncState AfterPull(SyncState state, long pulledVersion, string pulledSha256, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.BaseVersion = pulledVersion;
        state.LastSeenVersion = Math.Max(state.LastSeenVersion, pulledVersion);
        state.LastPushedSha256 = pulledSha256 ?? "";
        state.LastSyncAt = utcNow;
        return state;
    }


    public static SyncState ObserveVersion(SyncState state, long version)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.LastSeenVersion = Math.Max(state.LastSeenVersion, version);
        return state;
    }
}
