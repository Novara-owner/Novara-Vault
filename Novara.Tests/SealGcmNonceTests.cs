using System.Security.Cryptography;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealGcmNonceTests
{
    private static byte[] Bytes(int count, byte seed = 0)
    {
        var data = new byte[count];
        for (var i = 0; i < count; i++) data[i] = (byte)(seed + i * 7);
        return data;
    }






    [Fact]
    public void Encrypt_MatchesAesGcmDirectly()
    {
        var key = Bytes(32, 1);
        var nonce = Bytes(12, 2);
        var plain = Bytes(100, 3);
        var aad = Bytes(48, 4);

        var expectedCipher = new byte[plain.Length];
        var expectedTag = new byte[16];
        using (var gcm = new AesGcm(key, 16))
            gcm.Encrypt(nonce, plain, expectedCipher, expectedTag, aad);

        var actual = CryptoService.EncryptGcmWithNonce(plain, key, nonce, aad);

        Assert.Equal(expectedCipher, actual[..plain.Length].ToArray());
        Assert.Equal(expectedTag, actual[plain.Length..].ToArray());
    }

    [Fact]
    public void RoundTrip_ReturnsThePlaintext()
    {
        var key = Bytes(32, 5);
        var nonce = Bytes(12, 6);
        var plain = Bytes(4096, 7);
        var aad = Bytes(48, 8);

        var payload = CryptoService.EncryptGcmWithNonce(plain, key, nonce, aad);
        var back = CryptoService.DecryptGcmWithNonce(payload, key, nonce, aad);

        Assert.Equal(plain, back);
    }


    [Fact]
    public void EmptyPlaintext_ProducesATagOnlyAndRoundTrips()
    {
        var key = Bytes(32, 9);
        var nonce = Bytes(12, 10);

        var payload = CryptoService.EncryptGcmWithNonce(Array.Empty<byte>(), key, nonce, null);

        Assert.Equal(16, payload.Length);
        Assert.Empty(CryptoService.DecryptGcmWithNonce(payload, key, nonce, null));
    }

    [Fact]
    public void NoAssociatedData_IsSupported()
    {
        var key = Bytes(32, 11);
        var nonce = Bytes(12, 12);

        var payload = CryptoService.EncryptGcmWithNonce(Bytes(10), key, nonce);
        Assert.Equal(Bytes(10), CryptoService.DecryptGcmWithNonce(payload, key, nonce));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(0)]
    public void WrongNonceLength_Throws(int nonceLength)
    {
        var key = Bytes(32);
        Assert.Throws<ArgumentException>(() =>
            CryptoService.EncryptGcmWithNonce(Bytes(10), key, new byte[nonceLength]));
        Assert.Throws<ArgumentException>(() =>
            CryptoService.DecryptGcmWithNonce(new byte[32], key, new byte[nonceLength]));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(0)]
    public void WrongKeyLength_Throws(int keyLength)
    {
        Assert.Throws<ArgumentException>(() =>
            CryptoService.EncryptGcmWithNonce(Bytes(10), new byte[keyLength], new byte[12]));
        Assert.Throws<ArgumentException>(() =>
            CryptoService.DecryptGcmWithNonce(new byte[32], new byte[keyLength], new byte[12]));
    }

    [Fact]
    public void PayloadShorterThanATag_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            CryptoService.DecryptGcmWithNonce(new byte[15], Bytes(32), Bytes(12)));
    }

    [Fact]
    public void AnySingleByteOfThePayload_FailsAuthentication()
    {
        var key = Bytes(32, 13);
        var nonce = Bytes(12, 14);
        var aad = Bytes(48, 15);
        var payload = CryptoService.EncryptGcmWithNonce(Bytes(64, 16), key, nonce, aad);

        foreach (var index in new[] { 0, 31, 32, payload.Length - 1 })
        {
            var tampered = (byte[])payload.Clone();
            tampered[index] ^= 0x01;
            Assert.ThrowsAny<CryptographicException>(() =>
                CryptoService.DecryptGcmWithNonce(tampered, key, nonce, aad));
        }
    }

    [Fact]
    public void WrongNonce_FailsAuthentication()
    {
        var key = Bytes(32, 17);
        var nonce = Bytes(12, 18);
        var payload = CryptoService.EncryptGcmWithNonce(Bytes(32), key, nonce);

        var other = Bytes(12, 19);
        Assert.ThrowsAny<CryptographicException>(() =>
            CryptoService.DecryptGcmWithNonce(payload, key, other));
    }

    [Fact]
    public void WrongAssociatedData_FailsAuthentication()
    {
        var key = Bytes(32, 20);
        var nonce = Bytes(12, 21);
        var payload = CryptoService.EncryptGcmWithNonce(Bytes(32), key, nonce, Bytes(48, 22));

        Assert.ThrowsAny<CryptographicException>(() =>
            CryptoService.DecryptGcmWithNonce(payload, key, nonce, Bytes(48, 23)));
    }
}
