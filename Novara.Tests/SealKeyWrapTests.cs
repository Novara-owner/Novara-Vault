using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealKeyWrapTests
{
    private static byte[] Bytes(int count, byte seed = 0)
    {
        var data = new byte[count];
        for (var i = 0; i < count; i++) data[i] = (byte)(seed + i * 13);
        return data;
    }


    private static SealKdfParams Cheap() => SealKdfParams.Create(SealKdfParams.MemoryMinKiB, 1);

    private static byte[] BundleId(byte seed) => Bytes(16, seed);



    [Fact]
    public void Layout_MatchesTheContract()
    {
        Assert.Equal(8, SealKeyWrap.VersionOffset);
        Assert.Equal(9, SealKeyWrap.KdfIdOffset);
        Assert.Equal(10, SealKeyWrap.FlagsOffset);
        Assert.Equal(2, SealKeyWrap.FlagsSize);
        Assert.Equal(12, SealKeyWrap.KdfParamsOffset);
        Assert.Equal(24, SealKeyWrap.SaltOffset);
        Assert.Equal(40, SealKeyWrap.HeaderSize);
        Assert.Equal(40, SealKeyWrap.NonceOffset);
        Assert.Equal(52, SealKeyWrap.WrappedKeyOffset);
        Assert.Equal(84, SealKeyWrap.TagOffset);
        Assert.Equal(100, SealKeyWrap.FileSize);

        Assert.Equal(57, SealKeyWrap.HeaderSize + SealKeyWrap.BundleAadExtraSize);
        Assert.Equal(SealKeyWrap.SaltOffset, SealKeyWrap.KdfParamsOffset + SealKdfParams.Size);
        Assert.Equal(SealKeyWrap.HeaderSize, SealKeyWrap.SaltOffset + SealKdfParams.SaltSize);
    }

    [Theory]
    [InlineData(0, (byte)'N')]
    [InlineData(7, (byte)'P')]
    public void Magic_IsTheContractString(int offset, byte expected)
    {
        var wrap = SealKeyWrap.Wrap(Bytes(32, 1), "pw", Cheap());
        Assert.Equal(expected, wrap[offset]);
    }



    [Fact]
    public void RoundTrip_ReturnsTheSameVmk()
    {
        var vmk = Bytes(32, 2);
        var wrap = SealKeyWrap.Wrap(vmk, "correct horse battery staple", Cheap());

        Assert.Equal(SealKeyWrap.FileSize, wrap.Length);
        Assert.True(SealKeyWrap.TryUnwrap(wrap, "correct horse battery staple", out var back, out var outcome));
        Assert.Equal(SealKeyWrapOutcome.OkWeakened, outcome);
        Assert.Equal(vmk, back);
    }


    [Fact]
    public void TwoWraps_OfTheSameKeyDiffer()
    {
        var vmk = Bytes(32, 3);
        var first = SealKeyWrap.Wrap(vmk, "pw", Cheap());
        var second = SealKeyWrap.Wrap(vmk, "pw", Cheap());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void HundredRandomPasswords_AllRoundTrip()
    {
        var cheap = Cheap();
        for (var i = 0; i < 100; i++)
        {
            var password = "pw-" + Guid.NewGuid().ToString("N");
            var vmk = Bytes(32, (byte)i);

            var wrap = SealKeyWrap.Wrap(vmk, password, cheap);
            Assert.True(SealKeyWrap.TryUnwrap(wrap, password, out var back, out var outcome));
            Assert.Equal(SealKeyWrapOutcome.OkWeakened, outcome);
            Assert.Equal(vmk, back);
        }
    }


    [Fact]
    public void DefaultCost_OpensWithoutAWeakenedWarning()
    {
        var vmk = Bytes(32, 4);
        var wrap = SealKeyWrap.Wrap(vmk, "correct horse battery staple");
        Assert.True(SealKeyWrap.TryUnwrap(wrap, "correct horse battery staple", out var back, out var outcome));
        Assert.Equal(SealKeyWrapOutcome.Ok, outcome);
        Assert.Equal(vmk, back);
    }



    [Fact]
    public void WrongPassword_IsIndistinguishableFromCorruption()
    {
        var wrap = SealKeyWrap.Wrap(Bytes(32, 5), "right", Cheap());

        Assert.False(SealKeyWrap.TryUnwrap(wrap, "wrong", out var vmk, out var outcome));
        Assert.Equal(SealKeyWrapOutcome.WrongPasswordOrCorrupt, outcome);
        Assert.Empty(vmk);
    }


    [Fact]
    public void TrailingGarbage_IsRefused()
    {
        var wrap = SealKeyWrap.Wrap(Bytes(32, 6), "right", Cheap());
        var longer = new byte[wrap.Length + 1];
        wrap.CopyTo(longer, 0);

        Assert.False(SealKeyWrap.TryUnwrap(longer, "right", out var vmk, out var outcome));
        Assert.Equal(SealKeyWrapOutcome.WrongPasswordOrCorrupt, outcome);
        Assert.Empty(vmk);
    }


    [Fact]
    public void AnySingleByteOfHeaderOrBody_RefusesWithTheSameOutcome()
    {
        var password = "pw";
        var wrap = SealKeyWrap.Wrap(Bytes(32, 6), password, Cheap());

        foreach (var offset in new[] { 0, 7, 8, 9, 10, 11, 12, 23, 24, 39, 40, 51, 52, 83, 84, 99 })
        {
            var tampered = (byte[])wrap.Clone();
            tampered[offset] ^= 0x01;

            Assert.False(SealKeyWrap.TryUnwrap(tampered, password, out _, out var outcome));
            Assert.True(
                outcome is SealKeyWrapOutcome.WrongPasswordOrCorrupt or SealKeyWrapOutcome.VersionUnsupported,
                $"offset {offset} produced {outcome}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(9)]
    public void UnsupportedVersion_IsReportedSeparatelyFromCorruption(byte version)
    {
        var wrap = SealKeyWrap.Wrap(Bytes(32, 7), "pw", Cheap());
        wrap[SealKeyWrap.VersionOffset] = version;

        Assert.False(SealKeyWrap.TryUnwrap(wrap, "pw", out _, out var outcome));
        Assert.Equal(SealKeyWrapOutcome.VersionUnsupported, outcome);
    }

    [Fact]
    public void NonZeroFlags_AreRefused()
    {
        var wrap = SealKeyWrap.Wrap(Bytes(32, 8), "pw", Cheap());
        wrap[SealKeyWrap.FlagsOffset] = 1;

        Assert.False(SealKeyWrap.TryUnwrap(wrap, "pw", out _, out var outcome));
        Assert.Equal(SealKeyWrapOutcome.WrongPasswordOrCorrupt, outcome);
    }


    [Fact]
    public void HostileKdfCost_IsRefusedWithoutDeriving()
    {
        var wrap = SealKeyWrap.Wrap(Bytes(32, 9), "pw", Cheap());

        wrap[SealKeyWrap.KdfParamsOffset + 0] = 0x00;
        wrap[SealKeyWrap.KdfParamsOffset + 1] = 0x00;
        wrap[SealKeyWrap.KdfParamsOffset + 2] = 0x40;
        wrap[SealKeyWrap.KdfParamsOffset + 3] = 0x00;

        Assert.False(SealKeyWrap.TryUnwrap(wrap, "pw", out _, out var outcome));
        Assert.Equal(SealKeyWrapOutcome.WrongPasswordOrCorrupt, outcome);
    }

    [Fact]
    public void TruncatedWrap_IsRefused()
    {
        var wrap = SealKeyWrap.Wrap(Bytes(32, 10), "pw", Cheap());
        Assert.False(SealKeyWrap.TryUnwrap(wrap[..99], "pw", out _, out _));
    }



    [Fact]
    public void BundleWrap_OpensOnlyWithTheSameKindAndId()
    {
        var vmk = Bytes(32, 11);
        var id = BundleId(12);

        var wrap = SealKeyWrap.WrapForBundle(vmk, "pw", kind: 1, id, Cheap());

        Assert.True(SealKeyWrap.TryUnwrapFromBundle(wrap, "pw", 1, id, out var back, out _));
        Assert.Equal(vmk, back);

        Assert.False(SealKeyWrap.TryUnwrapFromBundle(wrap, "pw", 2, id, out _, out _));
        Assert.False(SealKeyWrap.TryUnwrapFromBundle(wrap, "pw", 1, BundleId(13), out _, out _));
    }


    [Fact]
    public void StandaloneAndBundleWraps_AreNotInterchangeable()
    {
        var vmk = Bytes(32, 14);

        var standalone = SealKeyWrap.Wrap(vmk, "pw", Cheap());
        Assert.False(SealKeyWrap.TryUnwrapFromBundle(standalone, "pw", 1, BundleId(15), out _, out _));

        var bundled = SealKeyWrap.WrapForBundle(vmk, "pw", 1, BundleId(15), Cheap());
        Assert.False(SealKeyWrap.TryUnwrap(bundled, "pw", out _, out _));
    }

    [Fact]
    public void BundleWrap_RejectsAWrongSizedIdentifier()
    {
        Assert.Throws<ArgumentException>(() =>
            SealKeyWrap.WrapForBundle(Bytes(32), "pw", 1, new byte[15], Cheap()));
    }







    [Fact]
    public void TheWrapNeverContainsThePlaintextMasterKey()
    {
        var vmk = Bytes(32, 16);
        var wrap = SealKeyWrap.Wrap(vmk, "pw", Cheap());

        for (var offset = 0; offset + vmk.Length <= wrap.Length; offset++)
            Assert.False(wrap.AsSpan(offset, vmk.Length).SequenceEqual(vmk),
                $"plaintext VMK found at offset {offset}");
    }

    [Fact]
    public void Wrap_RejectsAWrongSizedMasterKey()
    {
        Assert.Throws<ArgumentException>(() => SealKeyWrap.Wrap(new byte[31], "pw", Cheap()));
        Assert.Throws<ArgumentException>(() => SealKeyWrap.Wrap(new byte[33], "pw", Cheap()));
    }

    [Fact]
    public void Wrap_RejectsAnEmptyPassword()
    {
        Assert.Throws<ArgumentException>(() => SealKeyWrap.Wrap(Bytes(32), "", Cheap()));
    }
}
