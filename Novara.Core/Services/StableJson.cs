using System.Text.Json;

namespace Novara.Services;

internal static class StableJson
{
    private const int MaxAttempts = 4;
    private const int RetryDelayMs = 40;







    internal static byte[]? SerializeToUtf8BytesStable<T>(T value, JsonSerializerOptions options)
    {
        var first = TrySerialize(value, options);
        if (first == null) return null;
        for (var attempt = 1; attempt < MaxAttempts; attempt++)
        {
            var second = TrySerialize(value, options);
            if (second == null) return null;
            if (second.AsSpan().SequenceEqual(first)) return second;
            first = second;
            Thread.Sleep(RetryDelayMs);
        }
        return null;
    }

    private static byte[]? TrySerialize<T>(T value, JsonSerializerOptions options)
    {
        try { return JsonSerializer.SerializeToUtf8Bytes(value, options); }
        catch (Exception)
        {


            return null;
        }
    }
}
