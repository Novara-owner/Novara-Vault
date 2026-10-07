using System.Security.Cryptography;
using System.Text;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class CryptoKeyCacheTests
{
    private static byte[] Salt32() => RandomNumberGenerator.GetBytes(32);



    [Fact]
    public void SameParameters_RepeatedCallsAreStable()
    {
        CryptoService.ClearKeyCache();
        var salt = Salt32();
        var data = Encoding.UTF8.GetBytes("{\"probe\":1}");

        var first = CryptoService.EncryptGcm(data, "pw-cache-hit", salt, 100_000);
        var second = CryptoService.EncryptGcm(data, "pw-cache-hit", salt, 100_000);

        Assert.NotEqual(first, second);
        Assert.Equal(data, CryptoService.DecryptGcm(first, "pw-cache-hit", salt, 100_000));
        Assert.Equal(data, CryptoService.DecryptGcm(second, "pw-cache-hit", salt, 100_000));
    }



    [Fact]
    public void DifferentParameters_StayIndependent()
    {
        CryptoService.ClearKeyCache();
        var saltA = Salt32();
        var saltB = Salt32();
        var data = Encoding.UTF8.GetBytes("independent");

        var c1 = CryptoService.EncryptGcm(data, "pw-A", saltA, 1000);
        var c2 = CryptoService.EncryptGcm(data, "pw-B", saltA, 1000);
        var c3 = CryptoService.EncryptGcm(data, "pw-A", saltB, 1000);
        var c4 = CryptoService.EncryptGcm(data, "pw-A", saltA, 2000);

        Assert.Equal(data, CryptoService.DecryptGcm(c1, "pw-A", saltA, 1000));
        Assert.Equal(data, CryptoService.DecryptGcm(c2, "pw-B", saltA, 1000));
        Assert.Equal(data, CryptoService.DecryptGcm(c3, "pw-A", saltB, 1000));
        Assert.Equal(data, CryptoService.DecryptGcm(c4, "pw-A", saltA, 2000));

        Assert.ThrowsAny<CryptographicException>(() => CryptoService.DecryptGcm(c2, "pw-A", saltA, 1000));
        Assert.ThrowsAny<CryptographicException>(() => CryptoService.DecryptGcm(c4, "pw-A", saltA, 1000));
    }


    [Fact]
    public void CachedDerivation_RoundTripsExactly()
    {
        CryptoService.ClearKeyCache();
        var salt = Salt32();
        var plain = Encoding.UTF8.GetBytes("{\"title\":\"便签\",\"body\":\"round-trip\"}");

        var first = CryptoService.EncryptGcm(plain, "pw-rt", salt, 100_000);
        var second = CryptoService.EncryptGcm(plain, "pw-rt", salt, 100_000);
        Assert.NotEqual(first, second);
        Assert.Equal(plain, CryptoService.DecryptGcm(first, "pw-rt", salt, 100_000));
        Assert.Equal(plain, CryptoService.DecryptGcm(second, "pw-rt", salt, 100_000));
    }



    [Fact]
    public void ClearKeyCache_StillRoundTripsWithSameParameters()
    {
        CryptoService.ClearKeyCache();
        var salt = Salt32();
        var data = new byte[] { 9 };
        var before = CryptoService.EncryptGcm(data, "pw-clear", salt, 1000);
        Assert.Equal(data, CryptoService.DecryptGcm(before, "pw-clear", salt, 1000));

        CryptoService.ClearKeyCache();
        var after = CryptoService.EncryptGcm(data, "pw-clear", salt, 1000);


        Assert.Equal(data, CryptoService.DecryptGcm(after, "pw-clear", salt, 1000));
        Assert.Equal(data, CryptoService.DecryptGcm(before, "pw-clear", salt, 1000));
    }


    [Fact]
    public void OverCapacity_StillCorrect()
    {
        CryptoService.ClearKeyCache();
        var data = Encoding.UTF8.GetBytes("x");
        var salts = new byte[6][];
        for (var i = 0; i < salts.Length; i++)
        {
            salts[i] = Salt32();
            CryptoService.EncryptGcm(data, "pw-cap", salts[i], 1000);
        }

        for (var i = 0; i < salts.Length; i++)
            Assert.Equal(data, CryptoService.DecryptGcm(
                CryptoService.EncryptGcm(data, "pw-cap", salts[i], 1000), "pw-cap", salts[i], 1000));
    }









    [Fact]
    public void SecondDerivationWithSameParameters_IsObservablyCached()
    {
        CryptoService.ClearKeyCache();
        var salt = Salt32();
        var data = Encoding.UTF8.GetBytes("cache-observable");

        var (swMiss, swHit) = MeasureHitVsMiss(salt, data);




        if (swHit.ElapsedMilliseconds * 2 >= swMiss.ElapsedMilliseconds)
        {
            CryptoService.ClearKeyCache();
            (swMiss, swHit) = MeasureHitVsMiss(salt, data);
        }


        Assert.True(swHit.ElapsedMilliseconds * 2 < swMiss.ElapsedMilliseconds,
            $"缓存未生效：首次(未命中)={swMiss.ElapsedMilliseconds}ms 第二次(应命中)={swHit.ElapsedMilliseconds}ms");
    }

    private static (System.Diagnostics.Stopwatch Miss, System.Diagnostics.Stopwatch Hit) MeasureHitVsMiss(byte[] salt, byte[] data)
    {
        var swMiss = System.Diagnostics.Stopwatch.StartNew();
        CryptoService.EncryptGcm(data, "pw-observe", salt, CryptoService.CurrentIterations);
        swMiss.Stop();

        var swHit = System.Diagnostics.Stopwatch.StartNew();
        CryptoService.EncryptGcm(data, "pw-observe", salt, CryptoService.CurrentIterations);
        swHit.Stop();
        return (swMiss, swHit);
    }
}
