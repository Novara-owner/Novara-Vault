namespace Novara.Services;

internal static class SealToast
{
    private const int ResultHoldMilliseconds = 2500;

    private static bool _active;


    public static bool IsActive => _active;







    public static void Begin(string message)
    {
        if (_active)
        {
            _active = false;
            App.ClearToast();
        }

        _active = true;
        App.ShowToast(message, persistent: true);
    }


    public static void Update(string message)
    {
        if (!_active) return;
        App.UpdateToast(message);
    }


    public static void Finish(string message)
    {
        if (_active)
        {
            _active = false;
            App.ClearToast();
        }

        App.ShowTimedToast(message, ResultHoldMilliseconds);
    }


    public static void Abort()
    {
        if (!_active) return;
        _active = false;
        App.ClearToast();
    }
}
