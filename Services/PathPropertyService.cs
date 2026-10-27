using Microsoft.UI.Xaml;

namespace Novara.Services;

public enum PathPropertyPhase { Scanning, Completed }



public sealed record PathPropertyUpdate(
    Guid Id, string Path, PathPropertyPhase Phase,
    PathPropertyResult? Result, long LiveBytes, long LiveFiles, long LiveFolders);

public static class PathPropertyService
{
    private sealed class Entry
    {
        public string Path = "";
        public PathPropertyResult? Last;
        public bool Scanning;
        public long LiveBytes, LiveFiles, LiveFolders;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Guid, string> _targets = new();
    private static readonly Dictionary<string, CancellationTokenSource> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> _queue = new();
    private const int ScanConcurrency = 3;
    private static readonly Semaphore _work = new(0, int.MaxValue);

    private static volatile bool _running;
    private static bool _paused;
    private static DispatcherTimer? _refreshTimer;


    public static event Action<PathPropertyUpdate>? Updated;


    public static event Action<bool>? FeatureToggled;


    public static bool IsFeatureEnabled()
        => !App.McpBackground
           && App.Store is { IsLoaded: true } store
           && store.Database.AppSettings.PathPropertyRefreshMinutes >= 0;





    public static void Start()
    {
        if (App.McpBackground) return;
        lock (Gate)
        {
            if (_running) { ResumeFromPause(); return; }
            _running = true;
            _paused = false;
            for (int i = 0; i < ScanConcurrency; i++)
            {
                var t = new Thread(ScanLoop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "PathPropertyScan-" + i };
                t.Start();
            }
        }
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(4)); } catch { return; }
            if (!_running) return;
            BeginFullScan();
        });
        SyncRefreshTimer();
    }


    public static void PauseForLock()
    {
        lock (Gate)
        {
            _paused = true;
            _queue.Clear();
            foreach (var cts in _inFlight.Values) { try { cts.Cancel(); } catch { } }
            _inFlight.Clear();
            _cache.Clear();
        }
        _work.Release(ScanConcurrency);
    }


    private static void ResumeFromPause()
    {
        _paused = false;
        SyncRefreshTimer();
        BeginFullScan();
    }


    public static void Shutdown()
    {
        lock (Gate)
        {
            _running = false;
            _paused = false;
            _queue.Clear();
            foreach (var cts in _inFlight.Values) { try { cts.Cancel(); } catch { } }
            _inFlight.Clear();
            _cache.Clear();
            _targets.Clear();
        }
        _work.Release(ScanConcurrency);
        _refreshTimer?.Stop();
        _refreshTimer = null;
    }




    public static void BeginFullScan()
    {
        if (App.McpBackground || !_running || !IsFeatureEnabled()) return;
        var db = App.Store?.Database;
        if (db == null) return;
        var items = db.PathBackupItems
            .Where(x => !x.IsDeleted && !string.IsNullOrWhiteSpace(x.Path))
            .Select(x => (x.Id, x.Path))
            .ToList();
        SetTargets(items);
        RefreshAll();
    }




    public static void SetTargets(IReadOnlyList<(Guid Id, string Path)> items, bool visibleFirst = false)
    {
        if (!_running) return;
        if (!IsFeatureEnabled())
        {

            lock (Gate)
            {
                _targets.Clear();
                _queue.Clear();
            }
            return;
        }
        var orderedKeys = new List<string>();
        lock (Gate)
        {
            var fresh = new Dictionary<Guid, string>();
            foreach (var (id, path) in items)
            {
                var key = PathPropertyReader.NormalizeKey(path);
                if (key == null) continue;
                fresh[id] = key;
                if (!_cache.TryGetValue(key, out var entry))
                {
                    entry = new Entry { Path = path };
                    _cache[key] = entry;
                }
                else
                {
                    entry.Path = path;
                }
                if (visibleFirst) orderedKeys.Add(key);
                else if (entry.Last == null) orderedKeys.Add(key);
            }
            foreach (var staleId in _targets.Keys.Where(id => !fresh.ContainsKey(id)).ToList())
                _targets.Remove(staleId);
            foreach (var (id, key) in fresh) _targets[id] = key;

            foreach (var key in orderedKeys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_inFlight.ContainsKey(key)) continue;
                _queue.Remove(key);
                _queue.Insert(0, key);
            }
        }
        if (orderedKeys.Count > 0) _work.Release(Math.Min(orderedKeys.Count, ScanConcurrency));
    }




    public static void NotifyFeatureToggled()
    {
        if (!IsFeatureEnabled())
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;
            lock (Gate)
            {
                _queue.Clear();
                foreach (var cts in _inFlight.Values) { try { cts.Cancel(); } catch { } }
                _inFlight.Clear();
            }
            _work.Release(ScanConcurrency);
        }
        FeatureToggled?.Invoke(IsFeatureEnabled());
    }



    public static void RefreshAll()
    {
        if (!_running || !IsFeatureEnabled()) return;
        lock (Gate)
        {
            foreach (var key in _targets.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_inFlight.ContainsKey(key)) continue;
                _queue.Remove(key);
                _queue.Add(key);
            }
        }
        _work.Release(ScanConcurrency);
    }


    public static void RequestRescan(string path)
    {
        if (!_running || !IsFeatureEnabled()) return;
        var key = PathPropertyReader.NormalizeKey(path);
        if (key == null) return;
        lock (Gate)
        {
            if (_inFlight.ContainsKey(key)) return;
            _queue.Remove(key);
            _queue.Add(key);
        }
        _work.Release(1);
    }





    public static void SyncRefreshTimer()
    {
        int minutes = App.McpBackground || App.Store is not { IsLoaded: true }
            ? 0
            : App.Store.Database.AppSettings.PathPropertyRefreshMinutes;
        if (minutes <= 0)
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;
            return;
        }
        if (_refreshTimer != null)
        {
            _refreshTimer.Interval = TimeSpan.FromMinutes(minutes);
            return;
        }
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(minutes) };
        _refreshTimer.Tick += (_, _) =>
        {
            if (_running && !_paused) RefreshAll();
        };
        _refreshTimer.Start();
    }




    public static bool TryGet(string path, out PathPropertyResult? result, out bool scanning, out long liveBytes, out long liveFiles, out long liveFolders)
    {
        var key = PathPropertyReader.NormalizeKey(path);
        if (key == null) { result = null; scanning = false; liveBytes = liveFiles = liveFolders = 0; return false; }
        lock (Gate)
        {
            if (!_cache.TryGetValue(key, out var entry)) { result = null; scanning = false; liveBytes = liveFiles = liveFolders = 0; return false; }
            result = entry.Last;
            scanning = entry.Scanning;
            liveBytes = entry.LiveBytes;
            liveFiles = entry.LiveFiles;
            liveFolders = entry.LiveFolders;
            return true;
        }
    }



    private static void ScanLoop()
    {
        while (true)
        {
            string key;
            lock (Gate)
            {
                if (!_running) return;
                if (_paused || _queue.Count == 0) { key = ""; }
                else { key = _queue[0]; _queue.RemoveAt(0); }
            }
            if (key == "") { _work.WaitOne(); continue; }

            string path;
            Entry entry;
            lock (Gate)
            {
                if (!_cache.TryGetValue(key, out entry!)) continue;
                path = entry.Path;
                if (_inFlight.ContainsKey(key)) continue;
                entry.Scanning = true;
                _inFlight[key] = new CancellationTokenSource();
            }

            try
            {
                var cts = _inFlight[key];
                var result = PathPropertyReader.Read(path, PathPropertyReader.DefaultTimeout,
                    progress =>
                    {
                        List<Guid> ids;
                        lock (Gate)
                        {
                            if (!_cache.TryGetValue(key, out var live) || live is null) return;
                            live.LiveBytes = progress.Bytes;
                            live.LiveFiles = progress.Files;
                            live.LiveFolders = progress.Folders;
                            ids = _targets.Where(kv => kv.Value == key).Select(kv => kv.Key).ToList();
                        }
                        foreach (var id in ids)
                            Updated?.Invoke(new PathPropertyUpdate(id, path, PathPropertyPhase.Scanning, null, progress.Bytes, progress.Files, progress.Folders));
                    }, cts.Token);

                List<Guid> doneIds;
                lock (Gate)
                {
                    if (_cache.TryGetValue(key, out var e2) && e2 is not null)
                    {
                        e2.Last = result;
                        e2.LiveBytes = result.Bytes ?? 0;
                        e2.LiveFiles = result.Files ?? 0;
                        e2.LiveFolders = result.Folders ?? 0;
                    }
                    doneIds = _targets.Where(kv => kv.Value == key).Select(kv => kv.Key).ToList();
                }
                foreach (var id in doneIds)
                    Updated?.Invoke(new PathPropertyUpdate(id, path, PathPropertyPhase.Completed, result, 0, 0, 0));
            }
            catch (OperationCanceledException)
            {

                lock (Gate)
                {
                    if (_cache.TryGetValue(key, out var e3) && e3 is not null) e3.Scanning = false;
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogNote("PathPropertyScan", $"{path}: {ex.Message}");
                List<Guid> errIds;
                PathPropertyResult? errResult;
                lock (Gate)
                {
                    if (_cache.TryGetValue(key, out var e4) && e4 is not null)
                    {
                        e4.Last = new PathPropertyResult(PathPropertyState.Error, null, null, null, null, null, false, false, ex.GetType().Name);
                        e4.Scanning = false;
                    }
                    errIds = _targets.Where(kv => kv.Value == key).Select(kv => kv.Key).ToList();
                    errResult = _cache.TryGetValue(key, out var e6) && e6 is not null ? e6.Last : null;
                }
                foreach (var id in errIds)
                    Updated?.Invoke(new PathPropertyUpdate(id, path, PathPropertyPhase.Completed, errResult, 0, 0, 0));
            }
            finally
            {
                lock (Gate)
                {
                    if (_inFlight.TryGetValue(key, out var finished))
                    {
                        finished.Dispose();
                        _inFlight.Remove(key);
                    }
                    if (_cache.TryGetValue(key, out var e5) && e5 is not null) e5.Scanning = _inFlight.ContainsKey(key);
                }
            }
        }
    }
}
