using Novara.Models;

namespace Novara.Services;


public enum SealTakeVerdict
{

    TakeAllowed = 0,


    Locked,


    Expired,


    RollbackSuspected,
}




























public static class SealTimeLock
{

    public const int RollbackToleranceHours = 24;





    public static bool IsRolledBack(DateTimeOffset nowUtc, long observedMaxUtc)
        => observedMaxUtc > 0
           && nowUtc.AddHours(RollbackToleranceHours).ToUnixTimeSeconds() < observedMaxUtc;


    public static long RaiseObserved(long current, DateTimeOffset nowUtc)
        => Math.Max(current, nowUtc.ToUnixTimeSeconds());





    public static bool IsExpired(SealedItem item, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!item.IsCapsule) return true;



        var expiry = item.ExpiryUtc ?? long.MaxValue;
        return nowUtc.ToUnixTimeSeconds() >= expiry;
    }





    public static SealTakeVerdict Evaluate(SealedItem item, DateTimeOffset nowUtc, long observedMaxUtc)
    {
        ArgumentNullException.ThrowIfNull(item);



        if (!item.IsCapsule) return SealTakeVerdict.TakeAllowed;

        if (IsRolledBack(nowUtc, observedMaxUtc)) return SealTakeVerdict.RollbackSuspected;

        var expiry = item.ExpiryUtc ?? long.MaxValue;
        return nowUtc.ToUnixTimeSeconds() >= expiry
            ? SealTakeVerdict.Expired
            : SealTakeVerdict.Locked;
    }


    public static bool CanTake(SealedItem item, DateTimeOffset nowUtc, long observedMaxUtc)
        => Evaluate(item, nowUtc, observedMaxUtc) is SealTakeVerdict.TakeAllowed or SealTakeVerdict.Expired;
}
