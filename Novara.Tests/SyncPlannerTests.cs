using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SyncPlannerTests
{
    private static SyncPlanInput Input(
        bool enabled = true,
        bool locked = false,
        bool restorePending = false,
        long remote = 0,
        long baseVersion = 0,
        long lastSeen = 0,
        string localSha = "a",
        string pushedSha = "a",
        bool restorePendingConfirm = false)
        => new(enabled, locked, restorePending, remote, baseVersion, lastSeen, localSha, pushedSha,
               RestorePendingConfirm: restorePendingConfirm);

    [Fact]
    public void Decide_StandsDownWhenDisabledLockedOrRestoring()
    {
        Assert.Equal(SyncAction.Disabled, SyncPlanner.Decide(Input(enabled: false, localSha: "b")));
        Assert.Equal(SyncAction.WaitUnlocked, SyncPlanner.Decide(Input(locked: true, remote: 5, localSha: "b")));
        Assert.Equal(SyncAction.RestorePending, SyncPlanner.Decide(Input(restorePending: true, remote: 5, localSha: "b")));


        Assert.Equal(SyncAction.WaitUnlocked, SyncPlanner.Decide(Input(locked: true, restorePending: true, remote: 9, baseVersion: 1, localSha: "b")));
    }







    [Fact]
    public void Decide_AsksInsteadOfPushing_WhileARestoreAwaitsItsDirection()
    {


        Assert.Equal(SyncAction.Push, SyncPlanner.Decide(
            Input(remote: 7, baseVersion: 7, localSha: "snapshot", pushedSha: "live")));

        Assert.Equal(SyncAction.Conflict, SyncPlanner.Decide(
            Input(remote: 7, baseVersion: 7, localSha: "snapshot", pushedSha: "live", restorePendingConfirm: true)));




        Assert.Equal(SyncAction.Conflict, SyncPlanner.Decide(
            Input(remote: 7, baseVersion: 7, localSha: "same", pushedSha: "same", restorePendingConfirm: true)));
    }

    [Fact]
    public void Decide_PullsAfterUnlocking_WhenLocalHasNothingUnpublished()
    {

        var locked = Input(locked: true, remote: 3, baseVersion: 1);
        Assert.Equal(SyncAction.WaitUnlocked, SyncPlanner.Decide(locked));

        var unlocked = locked with { Locked = false };
        Assert.Equal(SyncAction.Pull, SyncPlanner.Decide(unlocked));
    }

    [Fact]
    public void Decide_PushesOnlyWhenTheRemoteHasNotMoved()
    {
        Assert.Equal(SyncAction.Push, SyncPlanner.Decide(Input(remote: 4, baseVersion: 4, lastSeen: 4, localSha: "b", pushedSha: "a")));
        Assert.Equal(SyncAction.NoOp, SyncPlanner.Decide(Input(remote: 4, baseVersion: 4, lastSeen: 4)));
    }

    [Fact]
    public void Decide_ConflictsWhenBothSidesMoved()
    {
        Assert.Equal(SyncAction.Conflict, SyncPlanner.Decide(Input(remote: 5, baseVersion: 3, lastSeen: 3, localSha: "b", pushedSha: "a")));
    }

    [Fact]
    public void Decide_RejectsARollbackBelowTheWatermark()
    {

        Assert.Equal(SyncAction.RollbackRejected, SyncPlanner.Decide(Input(remote: 2, baseVersion: 7, lastSeen: 7)));

        Assert.Equal(SyncAction.RollbackRejected, SyncPlanner.Decide(Input(remote: 1, baseVersion: 4, lastSeen: 4)));

        Assert.Equal(SyncAction.RollbackRejected, SyncPlanner.Decide(Input(remote: 1, baseVersion: 4, lastSeen: 4, localSha: "b", pushedSha: "a")));
    }

    [Fact]
    public void Decide_HandlesTheFirstPush()
    {


        Assert.Equal(SyncAction.Push, SyncPlanner.Decide(Input(remote: 0, baseVersion: 0, lastSeen: 0, localSha: "abc", pushedSha: "")));


        Assert.Equal(SyncAction.NoOp, SyncPlanner.Decide(Input(remote: 1, baseVersion: 1, lastSeen: 1, localSha: "abc", pushedSha: "abc")));
        Assert.Equal(SyncAction.Push, SyncPlanner.Decide(Input(remote: 1, baseVersion: 1, lastSeen: 1, localSha: "def", pushedSha: "abc")));
    }

    [Fact]
    public void AfterPush_AdvancesTheBaseAndTheWatermark()
    {
        var state = new SyncState { BaseVersion = 3, LastSeenVersion = 3, LastPushedSha256 = "old" };
        var now = new DateTime(2026, 9, 11, 3, 0, 0, DateTimeKind.Utc);

        SyncStateUpdater.AfterPush(state, 4, "new", now);

        Assert.Equal(4, state.BaseVersion);
        Assert.Equal(4, state.LastSeenVersion);
        Assert.Equal("new", state.LastPushedSha256);
        Assert.Equal(now, state.LastSyncAt);
    }

    [Fact]
    public void AfterPull_AdoptsTheRemoteAsTheNewBaseline()
    {
        var state = new SyncState { BaseVersion = 2, LastSeenVersion = 2, LastPushedSha256 = "local" };
        var now = new DateTime(2026, 9, 11, 3, 5, 0, DateTimeKind.Utc);

        SyncStateUpdater.AfterPull(state, 6, "remote", now);

        Assert.Equal(6, state.BaseVersion);
        Assert.Equal(6, state.LastSeenVersion);
        Assert.Equal("remote", state.LastPushedSha256);
        Assert.Equal(now, state.LastSyncAt);
        Assert.Equal(SyncAction.NoOp, SyncPlanner.Decide(Input(remote: 6, baseVersion: state.BaseVersion, lastSeen: state.LastSeenVersion, localSha: "remote", pushedSha: state.LastPushedSha256)));
    }

    [Fact]
    public void ObserveVersion_OnlyEverRaisesTheWatermark()
    {
        var state = new SyncState { LastSeenVersion = 5 };
        SyncStateUpdater.ObserveVersion(state, 3);
        Assert.Equal(5, state.LastSeenVersion);
        SyncStateUpdater.ObserveVersion(state, 9);
        Assert.Equal(9, state.LastSeenVersion);
    }
}
