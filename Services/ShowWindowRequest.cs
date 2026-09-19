namespace Novara.Services;

public static class ShowWindowRequest
{
    public const string EventName =
#if DEBUG
        @"Local\Novara.ShowWindow.Dev";
#else
        @"Local\Novara.ShowWindow";
#endif

    private static EventWaitHandle? _evt;
    private static Thread? _thread;
    private static volatile bool _running;

    public static void StartListening(Action handler)
    {
        try
        {
            _evt = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            _running = true;
            _thread = new Thread(() =>
            {
                while (_running)
                {
                    try
                    {
                        if (_evt.WaitOne(5000))
                        {
                            try { handler(); } catch { }
                        }
                    }
                    catch { }
                }
            })
            { IsBackground = true };
            _thread.Start();
        }
        catch {  }
    }






    public static void Raise()
    {
        try
        {
            using var evt = EventWaitHandle.OpenExisting(EventName);
            evt.Set();
        }
        catch {  }
    }
}
