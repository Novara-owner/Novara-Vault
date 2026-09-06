using System;
using System.Collections.Generic;

namespace Novara.Services;

/// <summary>
/// 9.3: network activity transparency (Privacy Dashboard step one). The API check entries report
/// Begin/End pairs; the main window subscribes StateChanged to drive the title-bar badge and reads
/// Recent for the activity panel. In-memory only by design - visited domains are private and the
/// trail is meant as instant transparency, not a persistent log.
/// </summary>
public static class NetworkActivityService
{
    public sealed record ActivityEntry(DateTimeOffset Time, string KindKey, string Host, bool Success);
    private sealed record Session(string KindKey, string Host);

    private static readonly object _gate = new();
    private static readonly List<ActivityEntry> _recent = new();
    private static readonly List<Session> _stack = new();
    private const int MaxEntries = 50;

    public static event Action? StateChanged;
    public static event Action? LogAppended;

    public static bool IsActive
    {
        get { lock (_gate) return _stack.Count > 0; }
    }

    /// <summary>Top of the session stack (the innermost running activity), or null when idle.</summary>
    public static (string KindKey, string Host)? Current
    {
        get
        {
            lock (_gate)
            {
                return _stack.Count == 0 ? null : (_stack[^1].KindKey, _stack[^1].Host);
            }
        }
    }

    /// <summary>Most recent finished activities first (newest at index 0).</summary>
    public static IReadOnlyList<ActivityEntry> Recent
    {
        get { lock (_gate) return _recent.ToArray(); }
    }

    public static void ClearRecent()
    {
        lock (_gate) _recent.Clear();
        StateChanged?.Invoke();
    }

    /// <summary>Push a running activity. Host is extracted from any url form; empty stays empty.</summary>
    public static void Begin(string kindKey, string urlOrHost)
    {
        lock (_gate)
        {
            _stack.Add(new Session(kindKey, ExtractHost(urlOrHost)));
        }
        StateChanged?.Invoke();
    }

    /// <summary>Pop the innermost activity and append it to the recent trail.</summary>
    public static void End(bool success = true)
    {
        ActivityEntry? entry = null;
        lock (_gate)
        {
            if (_stack.Count > 0)
            {
                var s = _stack[^1];
                _stack.RemoveAt(_stack.Count - 1);
                entry = new ActivityEntry(DateTimeOffset.Now, s.KindKey, s.Host, success);
                _recent.Insert(0, entry);
                if (_recent.Count > MaxEntries) _recent.RemoveAt(_recent.Count - 1);
            }
        }
        if (entry != null)
        {
            StateChanged?.Invoke();
            LogAppended?.Invoke();
        }
    }

    public static string ExtractHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var s = url.Trim();
        if (Uri.TryCreate(s.Contains("://") ? s : "https://" + s, UriKind.Absolute, out var u))
            return u.Host;
        return s;
    }
}
