using System.Security.Cryptography;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class UpdateManifestVerifierTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP521);
    private readonly string _spki;
    private readonly byte[] _manifest;

    public UpdateManifestVerifierTests()
    {
        _spki = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
        _manifest = System.Text.Encoding.UTF8.GetBytes("{\"version\":\"11.0.0\",\"sha256\":\"AB\"}");
    }

    public void Dispose() => _key.Dispose();

    private byte[] Sign() => _key.SignData(_manifest, HashAlgorithmName.SHA512);

    [Fact]
    public void RoundTrip_SignWithKeyPair_VerifiesTrue()
    {
        Assert.True(UpdateManifestVerifier.Verify(_manifest, Sign(), _spki));
    }

    [Fact]
    public void TamperedManifest_FailsVerification()
    {
        var sig = Sign();
        var tampered = (byte[])_manifest.Clone();
        tampered[^3] ^= 0x01;
        Assert.False(UpdateManifestVerifier.Verify(tampered, sig, _spki));
    }

    [Fact]
    public void SignatureFromAnotherKey_FailsVerification()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP521);
        var sig = other.SignData(_manifest, HashAlgorithmName.SHA512);
        Assert.False(UpdateManifestVerifier.Verify(_manifest, sig, _spki));
    }

    [Fact]
    public void MalformedSignature_ReturnsFalseWithoutThrowing()
    {
        Assert.False(UpdateManifestVerifier.Verify(_manifest, new byte[] { 1, 2, 3 }, _spki));
        Assert.Null(UpdateManifestVerifier.DecodeSignature("not base64 !!"));
    }

    [Fact]
    public void EmbeddedTrustedKey_IsAValidP521Spki()
    {

        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(UpdateManifestVerifier.TrustedPublicKeySpkiBase64), out _);
        Assert.Equal(521, key.KeySize);
    }
}
