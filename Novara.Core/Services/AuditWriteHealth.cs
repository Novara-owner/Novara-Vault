namespace Novara.Services;










public static class AuditWriteHealth
{
    private static readonly object Gate = new();
    private static DateTime _lastReportUtc = DateTime.MinValue;
    private static int _suppressed;
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);



    public static Action<string>? OnFailure { get; set; }



    internal static void ResetForTests()
    {
        lock (Gate) { _lastReportUtc = DateTime.MinValue; _suppressed = 0; }
    }

    internal static void Report(string source, Exception ex)    {
        try
        {
            Action<string>? handler;
            string message;
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                if (_lastReportUtc != DateTime.MinValue && now - _lastReportUtc < MinInterval)
                {
                    _suppressed++;
                    return;
                }
                _lastReportUtc = now;
                var extra = _suppressed > 0 ? $" (+{_suppressed} more suppressed)" : "";
                _suppressed = 0;
                handler = OnFailure;
                message = $"{source}: {ex.GetType().Name}: {ex.Message}{extra}";
            }
            handler?.Invoke(message);
        }
        catch {  }
    }
}
