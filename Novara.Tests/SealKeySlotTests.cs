using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealKeySlotTests
{
    private static byte[] Bytes(int count, byte seed = 0)
    {
        var data = new byte[count];
        for (var i = 0; i < count; i++) data[i] = (byte)(seed + i * 11);
        return data;
    }

    private static byte[] ItemId() => Enumerable.Range(0, SealFormat.ItemIdSize).Select(i => (byte)(i + 1)).ToArray();


    private static byte[] PrefixFor(byte[]? itemId = null, ulong length = 4096, long sealedAt = 1_790_000_000)
    {
        var header = SealContainerHeader.Create(
            itemId ?? ItemId(), length, sealedAt, SealFormat.DefaultChunkSize, new byte[SealFormat.KeySlotSize]);
        var full = new byte[SealFormat.HeaderSize];
        header.WriteTo(full);
        return full[..SealFormat.AuthenticatedPrefixSize];
    }



    [Fact]
    public void SlotLayout_MatchesTheContract()
    {
        Assert.Equal(0, SealKeySlot.NonceOffset);
        Assert.Equal(12, SealKeySlot.WrappedKeyOffset);
        Assert.Equal(44, SealKeySlot.WrappedKeyOffsetEnd);
        Assert.Equal(44, SealKeySlot.TagOffset);
        Assert.Equal(60, SealKeySlot.ReservedOffset);
        Assert.Equal(4, SealKeySlot.ReservedSize);
        Assert.Equal(48, SealKeySlot.SealedPayloadSize);
        Assert.Equal(SealFormat.KeySlotSize, SealKeySlot.ReservedOffset + SealKeySlot.ReservedSize);
    }

    [Fact]
    public void Wrap_LeavesTheReservedRangeZero()
    {
        var slot = new byte[SealFormat.KeySlotSize];
        SealKeySlot.Wrap(Bytes(32, 1), Bytes(32, 2), PrefixFor(), slot);

        for (var i = SealKeySlot.ReservedOffset; i < SealFormat.KeySlotSize; i++)
            Assert.Equal(0, slot[i]);


        Assert.NotEqual(Bytes(32, 2), slot[SealKeySlot.WrappedKeyOffset..SealKeySlot.WrappedKeyOffsetEnd].ToArray());
        Assert.Contains(slot[..SealFormat.NonceSize].ToArray(), b => b != 0);
    }



    [Fact]
    public void RoundTrip_ReturnsTheContentKey()
    {
        var vmk = Bytes(32, 3);
        var ck = Bytes(32, 4);
        var prefix = PrefixFor();
        var slot = new byte[SealFormat.KeySlotSize];

        SealKeySlot.Wrap(vmk, ck, prefix, slot);

        Assert.True(SealKeySlot.TryUnwrap(vmk, slot, prefix, out var back, out var error));
        Assert.Equal(SealFormatError.None, error);
        Assert.Equal(ck, back);
    }

    [Fact]
    public void TwoWraps_UseDifferentNonces()
    {
        var vmk = Bytes(32, 5);
        var ck = Bytes(32, 6);
        var prefix = PrefixFor();
        var first = new byte[SealFormat.KeySlotSize];
        var second = new byte[SealFormat.KeySlotSize];

        SealKeySlot.Wrap(vmk, ck, prefix, first);
        SealKeySlot.Wrap(vmk, ck, prefix, second);

        Assert.NotEqual(first, second);
    }





    [Fact]
    public void WrapWithNonce_IsByteStable()
    {
        var vmk = Bytes(32, 7);
        var ck = Bytes(32, 8);
        var prefix = PrefixFor();
        var nonce = Bytes(12, 9);
        var first = new byte[SealFormat.KeySlotSize];
        var second = new byte[SealFormat.KeySlotSize];

        SealKeySlot.WrapWithNonce(vmk, ck, prefix, nonce, first);
        SealKeySlot.WrapWithNonce(vmk, ck, prefix, nonce, second);

        Assert.Equal(first, second);
        Assert.Equal(nonce, first[..SealFormat.NonceSize].ToArray());

        var other = new byte[SealFormat.KeySlotSize];
        SealKeySlot.WrapWithNonce(vmk, ck, prefix, Bytes(12, 10), other);
        Assert.NotEqual(first, other);
    }







    [Fact]
    public void EveryAuthenticatedPrefixByte_IsBoundIntoTheSlot()
    {
        var vmk = Bytes(32, 11);
        var ck = Bytes(32, 12);
        var prefix = PrefixFor();
        var slot = new byte[SealFormat.KeySlotSize];
        SealKeySlot.Wrap(vmk, ck, prefix, slot);

        foreach (var offset in new[] { 0, 7, 8, 9, 10, 11, 12, 27, 28, 35, 36, 43, 44, 47 })
        {
            var tampered = (byte[])prefix.Clone();
            tampered[offset] ^= 0x01;

            Assert.False(SealKeySlot.TryUnwrap(vmk, slot, tampered, out var back, out var error));
            Assert.Equal(SealFormatError.Authentication, error);
            Assert.Empty(back);
        }
    }

    [Fact]
    public void DifferentItemId_ProducesADifferentSlot()
    {
        var vmk = Bytes(32, 13);
        var ck = Bytes(32, 14);

        var a = new byte[SealFormat.KeySlotSize];
        var b = new byte[SealFormat.KeySlotSize];
        SealKeySlot.Wrap(vmk, ck, PrefixFor(), a);
        SealKeySlot.Wrap(vmk, ck, PrefixFor(ItemId().Select(x => (byte)(x ^ 0xFF)).ToArray()), b);

        Assert.False(SealKeySlot.TryUnwrap(vmk, a, PrefixFor(ItemId().Select(x => (byte)(x ^ 0xFF)).ToArray()), out _, out _));
    }



    [Fact]
    public void EditingNonceCipherOrTag_FailsAuthentication()
    {
        var vmk = Bytes(32, 15);
        var prefix = PrefixFor();
        var slot = new byte[SealFormat.KeySlotSize];
        SealKeySlot.Wrap(vmk, Bytes(32, 16), prefix, slot);

        foreach (var offset in new[]
                 {
                     SealKeySlot.NonceOffset,
                     SealKeySlot.NonceOffset + 11,
                     SealKeySlot.WrappedKeyOffset,
                     SealKeySlot.WrappedKeyOffsetEnd - 1,
                     SealKeySlot.TagOffset,
                     SealKeySlot.ReservedOffset - 1,
                 })
        {
            var tampered = (byte[])slot.Clone();
            tampered[offset] ^= 0x01;

            Assert.False(SealKeySlot.TryUnwrap(vmk, tampered, prefix, out _, out var error));
            Assert.Equal(SealFormatError.Authentication, error);
        }
    }







    [Fact]
    public void EveryReservedByte_MustBeZero()
    {
        var vmk = Bytes(32, 17);
        var prefix = PrefixFor();
        var slot = new byte[SealFormat.KeySlotSize];
        SealKeySlot.Wrap(vmk, Bytes(32, 18), prefix, slot);

        for (var offset = SealKeySlot.ReservedOffset; offset < SealFormat.KeySlotSize; offset++)
        {
            var tampered = (byte[])slot.Clone();
            tampered[offset] = 1;

            Assert.False(SealKeySlot.TryUnwrap(vmk, tampered, prefix, out _, out var error));
            Assert.Equal(SealFormatError.Reserved, error);
        }
    }

    [Fact]
    public void WrongVmk_FailsAuthentication()
    {
        var prefix = PrefixFor();
        var slot = new byte[SealFormat.KeySlotSize];
        SealKeySlot.Wrap(Bytes(32, 19), Bytes(32, 20), prefix, slot);

        Assert.False(SealKeySlot.TryUnwrap(Bytes(32, 21), slot, prefix, out _, out var error));
        Assert.Equal(SealFormatError.Authentication, error);
    }

    [Fact]
    public void ShortSlot_IsTooShort()
    {
        Assert.False(SealKeySlot.TryUnwrap(Bytes(32, 22), new byte[63], PrefixFor(), out _, out var error));
        Assert.Equal(SealFormatError.TooShort, error);
    }



    [Fact]
    public void Wrap_RejectsProgrammingErrors()
    {
        var prefix = PrefixFor();
        var slot = new byte[SealFormat.KeySlotSize];

        Assert.Throws<ArgumentException>(() => SealKeySlot.Wrap(new byte[31], Bytes(32), prefix, slot));
        Assert.Throws<ArgumentException>(() => SealKeySlot.Wrap(Bytes(32), new byte[31], prefix, slot));
        Assert.Throws<ArgumentException>(() => SealKeySlot.Wrap(Bytes(32), Bytes(32), new byte[47], slot));
        Assert.Throws<ArgumentException>(() => SealKeySlot.Wrap(Bytes(32), Bytes(32), prefix, new byte[63]));
        Assert.Throws<ArgumentException>(() => SealKeySlot.WrapWithNonce(Bytes(32), Bytes(32), prefix, new byte[11], slot));
    }








    [Fact]
    public void RoundTrip_AllocatesAConstantAmountPerCall()
    {
        var vmk = Bytes(32, 23);
        var ck = Bytes(32, 24);
        var prefix = PrefixFor();
        var slot = new byte[SealFormat.KeySlotSize];

        for (var i = 0; i < 200; i++) RoundTrip();

        const int rounds = 2000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < rounds; i++) RoundTrip();
        var perRound = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)rounds;

        Assert.True(perRound < 4096, $"wrap+unwrap allocated {perRound:F0} bytes per round trip");

        void RoundTrip()
        {
            SealKeySlot.Wrap(vmk, ck, prefix, slot);
            Assert.True(SealKeySlot.TryUnwrap(vmk, slot, prefix, out _, out _));
        }
    }
}
