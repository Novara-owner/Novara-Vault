namespace Novara.Services;






public static class CardGesturePolicy
{


    public const int LongPressMs = 750;


    public const double MoveTolerance = 16.0;


    public static bool ExceedsMoveTolerance(double dx, double dy)
        => dx * dx + dy * dy > MoveTolerance * MoveTolerance;


    public static bool IsLongPress(long elapsedMs) => elapsedMs >= LongPressMs;


    public static bool IsClick(long elapsedMs, bool moved) => !moved && elapsedMs < LongPressMs;
}
