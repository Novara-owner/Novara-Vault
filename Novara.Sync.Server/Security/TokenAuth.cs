using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Novara.Services;

namespace Novara.Sync.Server.Security;





public static class TokenAuth
{
    public const int SecretBytes = 32;


    public static string NewSpaceId() => ToBase64Url(RandomNumberGenerator.GetBytes(16));

    public static string NewDeviceId() => ToBase64Url(RandomNumberGenerator.GetBytes(16));


    public static string NewToken() => ToBase64Url(RandomNumberGenerator.GetBytes(SecretBytes));


    public static string NewEnrollmentSecret() => ToBase64Url(RandomNumberGenerator.GetBytes(SecretBytes));


    public const int ReadTokenBytes = 15;


    public const int ReadTokenChars = ReadTokenRules.Length;



    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";







    public static string NewReadToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(ReadTokenBytes);
        var chars = new char[ReadTokenChars];
        int buffer = 0, bits = 0, at = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                chars[at++] = CrockfordAlphabet[(buffer >> (bits - 5)) & 31];
                bits -= 5;
            }
        }
        return new string(chars);
    }





    public static string NormalizeReadToken(string? raw) => ReadTokenRules.Normalize(raw);


    public static string DisplayReadToken(string? token) => ReadTokenRules.Display(token);


    public static string Hash(string secret)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret ?? "")));


    public static bool Verify(string? presented, string? storedHash)
    {
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(storedHash)) return false;

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        byte[] expected;
        try { expected = Convert.FromBase64String(storedHash); }
        catch (FormatException) { return false; }

        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}






















public sealed class FailureRateLimiter
{
    private sealed class Window
    {
        public DateTime StartUtc;
        public int Failures;
    }

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly TimeSpan _window;














    private const int SweepThreshold = 2048;
    private static readonly long MinSweepIntervalTicks = TimeSpan.FromSeconds(1).Ticks;
    private long _lastSweepTicks;



    private int _trackedKeys;

    public FailureRateLimiter(int maxFailures = 10, TimeSpan? window = null)
    {
        MaxFailures = maxFailures < 1 ? 1 : maxFailures;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    public int MaxFailures { get; }



    internal int TrackedKeyCount => _trackedKeys;

    public bool IsBlocked(string key)
    {
        if (!_windows.TryGetValue(key, out var w)) return false;
        lock (w)
        {
            if (DateTime.UtcNow - w.StartUtc >= _window) { w.Failures = 0; w.StartUtc = DateTime.UtcNow; return false; }
            return w.Failures >= MaxFailures;
        }
    }

    public void RecordFailure(string key)
    {
        var fresh = !_windows.TryGetValue(key, out var w);
        w ??= _windows.GetOrAdd(key, _ => new Window { StartUtc = DateTime.UtcNow });
        if (fresh) Interlocked.Increment(ref _trackedKeys);
        lock (w)
        {
            if (DateTime.UtcNow - w.StartUtc >= _window) { w.StartUtc = DateTime.UtcNow; w.Failures = 0; }
            w.Failures++;
        }





        if (Volatile.Read(ref _trackedKeys) > SweepThreshold) SweepExpired();
    }

    public void Reset(string key)
    {
        if (_windows.TryRemove(key, out _)) Interlocked.Decrement(ref _trackedKeys);
    }


    private void SweepExpired()
    {
        var now = DateTime.UtcNow;


        var last = Interlocked.Read(ref _lastSweepTicks);
        if (now.Ticks - last < MinSweepIntervalTicks) return;
        if (Interlocked.CompareExchange(ref _lastSweepTicks, now.Ticks, last) != last) return;

        var removedCount = 0;
        foreach (var kv in _windows)
        {
            bool expired;

            lock (kv.Value) expired = now - kv.Value.StartUtc >= _window;
            if (expired && _windows.TryRemove(kv.Key, out _)) removedCount++;
        }
        if (removedCount > 0) Interlocked.Add(ref _trackedKeys, -removedCount);
    }
}
