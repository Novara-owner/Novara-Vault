using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace Novara.Services;










public static class UpdateAutoCheckService
{

    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(25);


    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);


    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(1);

    private const string LogTag = "UpdateAutoCheck";

    private static DispatcherTimer? _watchdog;
    private static DateTimeOffset? _dueAt;
    private static bool _firstTickPending;
    private static UpdateService.LatestRelease? _pendingRelease;
    private static bool _checking;
    private static CancellationTokenSource? _checkCts;



    private static bool IsEnabledNow()
        => !App.McpBackground
           && App.Store is { IsLoaded: true } store
           && store.Database.AppSettings.AutoCheckUpdates;




    public static void SyncAutoCheckTimer()
    {
        if (!IsEnabledNow())
        {


            Stop(keepSchedule: IsTemporarilySuspended());
            return;
        }
        if (_watchdog != null) return;
        var now = DateTimeOffset.Now;
        var firstEnable = _dueAt is null;
        var startupDue = now + StartupDelay;
        if (firstEnable)
        {

            var last = App.Store is { IsLoaded: true } store ? store.Database.AppSettings.LastCheckUtc : null;
            var earliest = last is { } lc
                ? new DateTimeOffset(DateTime.SpecifyKind(lc, DateTimeKind.Utc)).Add(Interval)
                : DateTimeOffset.MinValue;
            _dueAt = earliest > startupDue ? earliest : startupDue;
            _firstTickPending = earliest <= startupDue;
        }
        _watchdog = new DispatcherTimer { Interval = firstEnable && _firstTickPending ? StartupDelay : WatchdogInterval };
        _watchdog.Tick += (_, _) => OnTick();
        _watchdog.Start();
    }



    private static bool IsTemporarilySuspended()
        => App.McpBackground || App.Store is not { IsLoaded: true };



    private static void Stop(bool keepSchedule = false)
    {
        _watchdog?.Stop();
        _watchdog = null;
        _firstTickPending = false;

        try { _checkCts?.Cancel(); } catch { }
        if (!keepSchedule)
        {
            _dueAt = null;
            _pendingRelease = null;
        }
        App.MainWindow?.DismissAutoUpdateReminder();
    }



    public static void NotifyManualCheckSucceeded()
    {
        if (!IsEnabledNow()) return;
        _dueAt = DateTimeOffset.Now + Interval;
        RecordCheckUtc();
    }


    private static void RecordCheckUtc()
    {
        if (App.Store is { IsLoaded: true } store)
        {
            store.Database.AppSettings.LastCheckUtc = DateTime.UtcNow;
            _ = store.SaveAsync();
        }
    }

    private static void OnTick()
    {

        if (!IsEnabledNow()) { Stop(keepSchedule: IsTemporarilySuspended()); return; }
        var window = App.MainWindow;
        if (window == null) return;
        var firstTick = _firstTickPending;
        _firstTickPending = false;
        if (_watchdog is { } watchdog && watchdog.Interval != WatchdogInterval) watchdog.Interval = WatchdogInterval;


        if (_pendingRelease is { } pending)
        {
            window.ShowAutoUpdateReminder(pending);
            return;
        }


        if (_dueAt is { } due && (firstTick || UpdatePolicy.IsAutoCheckDue(due, DateTimeOffset.Now)))
        {
            if (!TryBeginCheck()) return;
            _dueAt = DateTimeOffset.Now + Interval;
            _ = RunCheckCoreAsync();
        }
    }


    private static bool TryBeginCheck()
    {
        if (_checking) return false;
        _checking = true;
        return true;
    }

    private static async Task RunCheckCoreAsync()
    {
        var cts = new CancellationTokenSource();
        _checkCts = cts;
        try
        {
            var result = await UpdateService.CheckLatestAsync(cts.Token);


            if (!IsEnabledNow() || _watchdog == null) return;
            if (result.Error != null)
            {

                CrashLogger.LogNote(LogTag, result.Error);
                return;
            }
            if (result.Latest == null || !result.HasNewer) { RecordCheckUtc(); return; }
            RecordCheckUtc();
            var pendingVersion = App.Store is { IsLoaded: true } store
                ? store.Database.AppSettings.UpdatePendingVersion
                : "";
            if (!UpdatePolicy.ShouldPromptForVersion(result.Latest.Version, pendingVersion, UpdateService.CurrentVersion))
                return;
            _pendingRelease = result.Latest;
            App.MainWindow?.ShowAutoUpdateReminder(result.Latest);
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote(LogTag, ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_checkCts, cts)) _checkCts = null;
            cts.Dispose();
            _checking = false;
        }
    }


    public static void CancelInFlight()
    {
        try { _checkCts?.Cancel(); } catch { }
    }



    public static void ClearPendingRelease() => _pendingRelease = null;
}
