using System.Text;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SyncVectorTests
{


    private const string SpaceKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private static readonly byte[] VersionSalt = Enumerable.Range(0x20, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] WrapSalt = Enumerable.Range(0x40, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] WrapNonce = Enumerable.Range(0x60, 12).Select(i => (byte)i).ToArray();
    private static readonly byte[] SealNonce = Enumerable.Range(0x70, 12).Select(i => (byte)i).ToArray();
    private static readonly byte[] SealBody = Enumerable.Range(0x00, 32).Select(i => (byte)i).ToArray();
    private const string LockPassword = "novara-test-123";
    private const string PlainJson = "{\"k\":\"v\"}";

    private const string ExpectedBlobKey = "03e3448e0e220afaedeb49ccc61cceb542254cd7e8b9aad14147de0134c39e9d";
    private const string ExpectedMacKey = "2a360f7c2357e907835bf42468080322fae5ff61d654a72afaacbf0236874f9c";


    private const string ExpectedContainerHeader =
        "4e4f564104010000e8030000202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f";

    private const string ExpectedContainerHex =
        "4e4f564104010000e8030000202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f" +
        "42ed021e9e4338579fd1ff30" + "e943b9b96e811cbfa9f20bd9bc0ac7ae" + "c4c09be449b326620094ad4d33812f37b94c6c548786a4e106ace1b351";

    private const string ExpectedPayloadSha256 = "4170d04d850406f74b618972795e61590464cf6853423cb3eb7c3d57538bd4c8";

    private const string ExpectedCanonical =
        "{\"sync\":1,\"crypto\":4,\"space\":\"sp_fixed_space\",\"version\":42,\"base\":41," +
        "\"device\":\"dev_fixed\",\"ts\":\"2026-09-11T09:36:15Z\"," +
        "\"payloadSha256\":\"4170d04d850406f74b618972795e61590464cf6853423cb3eb7c3d57538bd4c8\"}";

    private const string ExpectedEnvelopeMac = "R0M1jddaNjoCQ5jW7L85bC8q++E8f7Qj9GFG998xZzs=";

    private const string ExpectedWrapCt = "OTM8O1/EaxvFWl00I3Ac+7k5xeXrm9DGD52vcmBLaYL513M6eezcFeLeo2fnsqp/eNtC537QgRSJzzB1";







    private const string ExpectedSealContainerHex =
        "4e4f564104010000e8030000202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f" +
        "707172737475767778797a7b" + "3135e375e7c2da625d13dbf65850ac57" + "e32e42d030ac89f6c92cff80b04e659d161c19d5f6ec18bf81a4248869cb0497";

    [Fact]
    public void BlobKey_And_MacKey_MatchPinnedVectors()
    {
        Assert.Equal(ExpectedBlobKey, Convert.ToHexString(SyncKeyWrap.DeriveBlobKey(SpaceKey, VersionSalt)).ToLowerInvariant());
        Assert.Equal(ExpectedMacKey, Convert.ToHexString(SyncEnvelopeCodec.DeriveMacKey(SpaceKey)).ToLowerInvariant());
    }

    [Fact]
    public void Container_SelfRoundTrips_And_MatchesThePinnedHeaderLayout()
    {
        var container = SyncContainer.Seal(Encoding.UTF8.GetBytes(PlainJson), SpaceKey, VersionSalt);

        Assert.Equal(ExpectedContainerHeader,
            Convert.ToHexString(container.AsSpan(0, SyncContainer.HeaderSize).ToArray()).ToLowerInvariant());
        Assert.Equal(PlainJson, Encoding.UTF8.GetString(SyncContainer.Open(container, SpaceKey)));

        Assert.True(SyncContainer.TryReadHeader(container, out var version, out var iterations, out var salt));
        Assert.Equal(4, version);
        Assert.Equal(SyncKeyWrap.BlobIterations, iterations);
        Assert.Equal(Convert.ToBase64String(VersionSalt), Convert.ToBase64String(salt));
    }

    [Fact]
    public void PinnedContainer_Opens_And_ItsEnvelopeVectorsMatch()
    {
        var container = Convert.FromHexString(ExpectedContainerHex);
        Assert.Equal(PlainJson, Encoding.UTF8.GetString(SyncContainer.Open(container, SpaceKey)));

        var envelope = new SyncEnvelope
        {
            Space = "sp_fixed_space",
            Version = 42,
            Base = 41,
            Device = "dev_fixed",
            Ts = "2026-09-11T09:36:15Z",
            Payload = Convert.ToBase64String(container),
        };
        SyncEnvelopeCodec.Seal(envelope, SpaceKey);

        Assert.Equal(ExpectedPayloadSha256, envelope.PayloadSha256);
        Assert.Equal(ExpectedCanonical, Encoding.UTF8.GetString(SyncEnvelopeCodec.Canonical(envelope)));
        Assert.Equal(ExpectedEnvelopeMac, envelope.Mac);
        Assert.True(SyncEnvelopeCodec.Verify(envelope, SpaceKey));
        Assert.True(SyncEnvelopeCodec.Verify(SyncEnvelopeCodec.Deserialize(SyncEnvelopeCodec.Serialize(envelope)), SpaceKey));
    }

    [Fact]
    public void KeyWrap_Ciphertext_MatchesPinnedVector_And_StillUnwraps()
    {
        var record = SyncKeyWrap.WrapWith(SpaceKey, LockPassword, WrapSalt, WrapNonce);
        Assert.Equal(Convert.ToBase64String(WrapSalt), record.Salt);
        Assert.Equal(Convert.ToBase64String(WrapNonce), record.Nonce);
        Assert.Equal(ExpectedWrapCt, record.Ct);
        Assert.Equal(SpaceKey, SyncKeyWrap.Unwrap(record, LockPassword));
        Assert.Throws<InvalidDataException>(() => SyncKeyWrap.Unwrap(record, LockPassword + "x"));
    }

    [Fact]
    public void SealDirection_ContainerBytes_MatchThePinnedVector()
    {
        var container = SyncContainer.SealBody(SealBody, SpaceKey, VersionSalt, SealNonce);
        Assert.Equal(ExpectedSealContainerHex, Convert.ToHexString(container).ToLowerInvariant());

        Assert.True(SyncContainer.TryReadHeader(container, out var version, out var iterations, out var salt));
        Assert.Equal(4, version);
        Assert.Equal(SyncKeyWrap.BlobIterations, iterations);
    }






    [Fact]
    public void Rewrap_ReSealsTheSameSpaceKey_UnderTheNewPasswordOnly()
    {
        const string NewPassword = LockPassword + "-new";
        var json = SyncJson.SerializeKeyWrap(SyncKeyWrap.Wrap(SpaceKey, LockPassword));

        var rewrapped = SyncKeyWrap.Rewrap(json, LockPassword, NewPassword);

        Assert.NotNull(rewrapped);
        Assert.Equal(SpaceKey, SyncKeyWrap.Unwrap(SyncJson.DeserializeKeyWrap(rewrapped), NewPassword));
        Assert.Throws<InvalidDataException>(() => SyncKeyWrap.Unwrap(SyncJson.DeserializeKeyWrap(rewrapped), LockPassword));


        Assert.Null(SyncKeyWrap.Rewrap(json, LockPassword + "-wrong", NewPassword));

        Assert.Null(SyncKeyWrap.Rewrap(rewrapped, LockPassword, NewPassword));
    }
}
