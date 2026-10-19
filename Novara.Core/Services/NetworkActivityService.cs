using System;
using System.Collections.Generic;

namespace Novara.Services;







public static class NetworkActivityService
{
    public sealed record ActivityEntry(DateTimeOffset Time, string KindKey, string Host, bool Success);

    internal sealed class Session
    {
        internal Session(string kindKey, string host) { KindKey = kindKey; Host = host; }
        internal string KindKey { get; }
        internal string Host { get; }
    }



    public sealed class ActivityHandle
    {
        internal ActivityHandle(Session session) => Session = session;
        internal Session Session { get; }
    }

    private static readonly object _gate = new();
    private static readonly List<ActivityEntry> _recent = new();
    private static readonly List<Session> _stack = new();
    private const int MaxEntries = 50;



    public static bool IsActive
    {
        get { lock (_gate) return _stack.Count > 0; }
    }


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


    public static IReadOnlyList<ActivityEntry> Recent
    {
        get { lock (_gate) return _recent.ToArray(); }
    }

    public static void ClearRecent()
    {
        lock (_gate) _recent.Clear();
    }


    public static ActivityHandle Begin(string kindKey, string urlOrHost)
    {
        var session = new Session(kindKey, ExtractHost(urlOrHost));
        lock (_gate) _stack.Add(session);
        return new ActivityHandle(session);
    }


    public static void End(ActivityHandle handle, bool success = true)
    {
        if (handle is null) return;
        lock (_gate)
        {
            var index = _stack.LastIndexOf(handle.Session);
            if (index < 0) return;
            var s = _stack[index];
            _stack.RemoveAt(index);
            _recent.Insert(0, new ActivityEntry(DateTimeOffset.Now, s.KindKey, s.Host, success));
            if (_recent.Count > MaxEntries) _recent.RemoveAt(_recent.Count - 1);
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
