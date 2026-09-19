using System;
using System.Linq;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SyncViewRefreshTests
{


    [Theory]
    [InlineData(SyncRoundStatus.Pull, true)]
    [InlineData(SyncRoundStatus.AutoRebased, true)]
    [InlineData(SyncRoundStatus.Push, false)]
    [InlineData(SyncRoundStatus.NoOp, false)]
    [InlineData(SyncRoundStatus.Conflict, false)]
    [InlineData(SyncRoundStatus.RollbackRejected, false)]
    [InlineData(SyncRoundStatus.WaitUnlocked, false)]
    [InlineData(SyncRoundStatus.RestorePending, false)]
    [InlineData(SyncRoundStatus.Disabled, false)]
    [InlineData(SyncRoundStatus.TransportFailure, false)]
    [InlineData(SyncRoundStatus.Error, false)]
    public void Only_a_pull_rebuilds_the_view(SyncRoundStatus status, bool expected)
        => Assert.Equal(expected, SyncViewRefresh.Required(status));

    [Fact]
    public void Every_status_is_classified()
    {
        var classified = new[]
        {
            SyncRoundStatus.Pull, SyncRoundStatus.AutoRebased, SyncRoundStatus.Push, SyncRoundStatus.NoOp, SyncRoundStatus.Conflict,
            SyncRoundStatus.RollbackRejected, SyncRoundStatus.WaitUnlocked, SyncRoundStatus.RestorePending,
            SyncRoundStatus.Disabled, SyncRoundStatus.TransportFailure, SyncRoundStatus.Error,
        };

        Assert.Equal(
            Enum.GetValues<SyncRoundStatus>().OrderBy(value => value),
            classified.OrderBy(value => value));
    }
}
