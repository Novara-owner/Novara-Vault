using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealTimeLockTests
{
    private const long SealedAt = 1_790_000_000;
    private const long ThirtyDays = 30L * 24 * 60 * 60;
    private const long TwentyYears = 7300L * 24 * 60 * 60;


    private const long SixtySeconds = 60;
    private const long FortyYears = 40L * 365 * 24 * 60 * 60;

    private static SealedItem Item(string kind, long? expiryUtc = null) => new()
    {
        Id = new string('a', 32),
        DisplayName = "条目",
        OriginalName = "原件.pdf",
        SourcePath = @"C:\Users\example\原件.pdf",
        Size = 1024,
        SealedAtUtc = SealedAt,
        Kind = kind,
        ExpiryUtc = expiryUtc,
        ContentSha256 = new string('b', 64),
    };

    private static SealedItem Capsule(long expiryUtc) => Item(SealedItemKind.Capsule, expiryUtc);

    private static SealedItem Ordinary() => Item(SealedItemKind.Normal);

    private static DateTimeOffset At(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds);



    [Fact]
    public void ThirtyDayCapsule_OpensOnTheExactSecond()
    {
        var capsule = Capsule(SealedAt + ThirtyDays);

        Assert.Equal(SealTakeVerdict.Locked, SealTimeLock.Evaluate(capsule, At(SealedAt + ThirtyDays - 1), 0));
        Assert.Equal(SealTakeVerdict.Expired, SealTimeLock.Evaluate(capsule, At(SealedAt + ThirtyDays), 0));
    }

    [Fact]
    public void TwentyYearCapsule_BehavesTheSameWay()
    {
        var capsule = Capsule(SealedAt + TwentyYears);


        Assert.Equal(SealTakeVerdict.Locked, SealTimeLock.Evaluate(capsule, At(SealedAt + TwentyYears - 1), 0));
        Assert.Equal(SealTakeVerdict.Expired, SealTimeLock.Evaluate(capsule, At(SealedAt + TwentyYears + 1), 0));
    }

    [Fact]
    public void SixtySecondCapsule_OpensOnTheExactSecond()
    {
        var capsule = Capsule(SealedAt + SixtySeconds);

        Assert.Equal(SealTakeVerdict.Locked, SealTimeLock.Evaluate(capsule, At(SealedAt + SixtySeconds - 1), 0));
        Assert.Equal(SealTakeVerdict.Expired, SealTimeLock.Evaluate(capsule, At(SealedAt + SixtySeconds), 0));
    }

    [Fact]
    public void FortyYearCapsule_BehavesTheSameWay()
    {
        var capsule = Capsule(SealedAt + FortyYears);

        Assert.Equal(SealTakeVerdict.Locked, SealTimeLock.Evaluate(capsule, At(SealedAt + FortyYears - 1), 0));
        Assert.Equal(SealTakeVerdict.Expired, SealTimeLock.Evaluate(capsule, At(SealedAt + FortyYears + 1), 0));
    }

    [Fact]
    public void OrdinaryItem_IsAlwaysTakeable()
    {
        Assert.Equal(SealTakeVerdict.TakeAllowed, SealTimeLock.Evaluate(Ordinary(), At(SealedAt), 0));
        Assert.True(SealTimeLock.CanTake(Ordinary(), At(SealedAt), 0));
    }







    [Fact]
    public void LeapDay_DoesNotShiftTheExpiry()
    {
        var sealedAt = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var expiry = new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.Zero);
        var capsule = Capsule(expiry.ToUnixTimeSeconds());

        Assert.Equal(SealTakeVerdict.Locked,
            SealTimeLock.Evaluate(capsule, new DateTimeOffset(2028, 2, 28, 23, 59, 59, TimeSpan.Zero), 0));
        Assert.Equal(SealTakeVerdict.Expired,
            SealTimeLock.Evaluate(capsule, expiry, 0));

        Assert.True(sealedAt < expiry);
    }


    [Fact]
    public void TimeZoneOfTheInput_DoesNotChangeTheVerdict()
    {
        var capsule = Capsule(SealedAt + ThirtyDays);
        var instant = At(SealedAt + ThirtyDays);

        Assert.Equal(
            SealTimeLock.Evaluate(capsule, instant, 0),
            SealTimeLock.Evaluate(capsule, instant.ToOffset(TimeSpan.FromHours(8)), 0));

        Assert.Equal(
            SealTimeLock.Evaluate(capsule, instant.AddSeconds(-1), 0),
            SealTimeLock.Evaluate(capsule, instant.AddSeconds(-1).ToOffset(TimeSpan.FromHours(-5)), 0));
    }



    [Fact]
    public void ExactlyTwentyFourHoursBehind_IsTolerated()
    {
        var now = At(SealedAt);
        var observed = SealedAt + 24 * 60 * 60;

        Assert.False(SealTimeLock.IsRolledBack(now, observed));
    }

    [Fact]
    public void OneSecondBeyondTheTolerance_IsSuspicious()
    {
        var now = At(SealedAt);
        var observed = SealedAt + 24 * 60 * 60 + 1;

        Assert.True(SealTimeLock.IsRolledBack(now, observed));
    }

    [Fact]
    public void NoObservedMaximum_MeansNoSuspicion()
    {
        Assert.False(SealTimeLock.IsRolledBack(At(1_000_000), 0));
    }





    [Fact]
    public void RollbackOutranksLocked()
    {
        var capsule = Capsule(SealedAt + TwentyYears);
        var now = At(SealedAt);
        var observed = SealedAt + 2 * 24 * 60 * 60;


        Assert.Equal(SealTakeVerdict.Locked, SealTimeLock.Evaluate(capsule, now, 0));


        Assert.Equal(SealTakeVerdict.RollbackSuspected, SealTimeLock.Evaluate(capsule, now, observed));
        Assert.False(SealTimeLock.CanTake(capsule, now, observed));
    }







    [Fact]
    public void RollbackGatesCapsulesOnly_OrdinaryItemsStayTakeable()
    {
        var now = At(SealedAt);
        var observed = SealedAt + 3 * 24 * 60 * 60;

        Assert.True(SealTimeLock.IsRolledBack(now, observed), "the rollback itself is still visible to the UI");

        Assert.Equal(SealTakeVerdict.TakeAllowed, SealTimeLock.Evaluate(Ordinary(), now, observed));
        Assert.True(SealTimeLock.CanTake(Ordinary(), now, observed));

        var capsule = Capsule(SealedAt + ThirtyDays);
        Assert.Equal(SealTakeVerdict.RollbackSuspected, SealTimeLock.Evaluate(capsule, now, observed));
        Assert.False(SealTimeLock.CanTake(capsule, now, observed));
    }



    [Fact]
    public void RaiseObserved_NeverGoesBackwards()
    {
        var random = new Random(20261030);
        var current = 0L;
        var expected = 0L;

        for (var i = 0; i < 1000; i++)
        {
            var moment = random.NextInt64(0, SealedAt + 10_000_000);
            var raised = SealTimeLock.RaiseObserved(current, DateTimeOffset.FromUnixTimeSeconds(moment));

            expected = Math.Max(expected, moment);
            Assert.Equal(expected, raised);
            Assert.True(raised >= current, "observed maximum must never decrease");

            current = raised;
        }
    }

    [Fact]
    public void RaiseObserved_AcceptsAnEarlierMomentWithoutLowering()
    {
        Assert.Equal(1000, SealTimeLock.RaiseObserved(1000, At(500)));
        Assert.Equal(1500, SealTimeLock.RaiseObserved(1000, At(1500)));
    }



    [Fact]
    public void EveryVerdict_IsReachable()
    {
        var now = At(SealedAt);
        var capsule = Capsule(SealedAt + ThirtyDays);

        var verdicts = new[]
        {
            SealTimeLock.Evaluate(Ordinary(), now, 0),
            SealTimeLock.Evaluate(capsule, now, 0),
            SealTimeLock.Evaluate(capsule, At(SealedAt + ThirtyDays), 0),
            SealTimeLock.Evaluate(capsule, now, SealedAt + 3 * 24 * 60 * 60),
            SealTimeLock.Evaluate(Ordinary(), At(SealedAt + ThirtyDays), 0),
            SealTimeLock.Evaluate(Ordinary(), now, SealedAt + 3 * 24 * 60 * 60),
        };

        Assert.Equal(SealTakeVerdict.TakeAllowed, verdicts[0]);
        Assert.Equal(SealTakeVerdict.Locked, verdicts[1]);
        Assert.Equal(SealTakeVerdict.Expired, verdicts[2]);
        Assert.Equal(SealTakeVerdict.RollbackSuspected, verdicts[3]);
        Assert.Equal(SealTakeVerdict.TakeAllowed, verdicts[4]);

        Assert.Equal(SealTakeVerdict.TakeAllowed, verdicts[5]);
    }

}
