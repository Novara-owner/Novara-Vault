using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Novara.Services;
using Xunit;
using Xunit.Abstractions;

namespace Novara.Tests;

public class KdfCacheBenchmark
{
    private readonly ITestOutputHelper _out;
    public KdfCacheBenchmark(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "量化工具，按需手动运行（去掉 Skip 或改 filters）；不进常规回归")]
    public void MeasurePerSaveKdfCost()
    {
        const int iterations = 3_000_000;
        var salt = RandomNumberGenerator.GetBytes(32);
        var payload = Encoding.UTF8.GetBytes("{\"probe\":\"" + new string('x', 4096) + "\"}");


        CryptoService.ClearKeyCache();
        var swMiss = Stopwatch.StartNew();
        for (var i = 0; i < 3; i++)
        {
            CryptoService.ClearKeyCache();
            CryptoService.EncryptGcm(payload, "bench-pw", salt, iterations);
        }
        swMiss.Stop();
        var perSaveBefore = swMiss.Elapsed.TotalMilliseconds / 3.0;


        CryptoService.ClearKeyCache();
        CryptoService.EncryptGcm(payload, "bench-pw", salt, iterations);
        var swHit = Stopwatch.StartNew();
        for (var i = 0; i < 3; i++) CryptoService.EncryptGcm(payload, "bench-pw", salt, iterations);
        swHit.Stop();
        var perSaveAfter = swHit.Elapsed.TotalMilliseconds / 3.0;

        var (hits, misses) = CryptoService.CacheCounters();
        _out.WriteLine("迭代数            = {0:N0}", iterations);
        _out.WriteLine("修复前 每次保存    = {0:F1} ms", perSaveBefore);
        _out.WriteLine("修复后 每次保存    = {0:F3} ms", perSaveAfter);
        _out.WriteLine("倍数              = {0:N0}x", perSaveBefore / Math.Max(perSaveAfter, 0.0001));
        _out.WriteLine("命中/未命中        = {0}/{1}", hits.Length, misses.Length);

        _out.WriteLine("外推：慢 10 倍的机器，修复前 ≈ {0:F1} s/次保存，修复后 ≈ 0", perSaveBefore * 10 / 1000.0);
    }
}
