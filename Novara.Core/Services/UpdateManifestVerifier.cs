using System.Security.Cryptography;

namespace Novara.Services;

public static class UpdateManifestVerifier
{

    public const string TrustedPublicKeySpkiBase64 =
        "MIGbMBAGByqGSM49AgEGBSuBBAAjA4GGAAQBTUj4GeD3Ro0l9fWJnO16ZxMr/Lr6Z/i3ZNiHEETcAyo2GLCNYP23wu34aRkunY8CuSHb+SHGqQbEtcwUciR4zU8B1EGyhitFmPsYm7XupvN0NxeG/l6dF/lTo1Tl3nVxAdyUd6LTSQr+r01hc6WRxaS24EkhhQkCeqNRFcfjMigg4aE=";


    public static byte[]? DecodeSignature(string sigText)
    {
        try { return Convert.FromBase64String((sigText ?? string.Empty).Trim()); }
        catch { return null; }
    }


    public static bool Verify(byte[] manifestBytes, byte[] signature)
        => Verify(manifestBytes, signature, TrustedPublicKeySpkiBase64);


    public static bool Verify(byte[] manifestBytes, byte[] signature, string trustedSpkiBase64)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(trustedSpkiBase64), out _);
            return ecdsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA512);
        }
        catch { return false; }
    }
}
