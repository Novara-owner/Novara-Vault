using System.Buffers.Binary;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealFormatTests
{
    private const long SealedAt = 1_790_000_000;

    private static byte[] IdA() => Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();

    private static byte[] Slot() => Enumerable.Range(0, 64).Select(i => (byte)(0xA0 + i)).ToArray();

    private static SealContainerHeader Header(byte flags = 0, ulong length = 4096, int chunkSize = SealFormat.DefaultChunkSize)
        => SealContainerHeader.Create(IdA(), length, SealedAt, chunkSize, Slot(), flags);

    private static byte[] Bytes(SealContainerHeader header)
    {
        var buffer = new byte[SealFormat.HeaderSize];
        header.WriteTo(buffer);
        return buffer;
    }

    private static byte[] Bytes(byte flags = 0, ulong length = 4096, int chunkSize = SealFormat.DefaultChunkSize)
        => Bytes(Header(flags, length, chunkSize));







    [Fact]
    public void FieldOffsets_MatchThePublishedTable()
    {
        Assert.Equal(0, SealFormat.MagicOffset);
        Assert.Equal(8, SealFormat.VersionOffset);
        Assert.Equal(9, SealFormat.AlgoIdOffset);
        Assert.Equal(10, SealFormat.FlagsOffset);
        Assert.Equal(11, SealFormat.ReservedOffset);
        Assert.Equal(12, SealFormat.ItemIdOffset);
        Assert.Equal(28, SealFormat.PlaintextLengthOffset);
        Assert.Equal(36, SealFormat.SealedAtUtcOffset);
        Assert.Equal(44, SealFormat.ChunkSizeOffset);
        Assert.Equal(48, SealFormat.KeySlotOffset);
        Assert.Equal(112, SealFormat.TrailerReservedOffset);

        Assert.Equal(128, SealFormat.HeaderSize);
        Assert.Equal(64, SealFormat.KeySlotSize);
        Assert.Equal(16, SealFormat.TrailerReservedSize);
        Assert.Equal(16, SealFormat.ItemIdSize);
        Assert.Equal(48, SealFormat.AuthenticatedPrefixSize);
        Assert.Equal(21, SealFormat.BlockAadSize);
    }

    [Fact]
    public void Magic_IsEightAsciiBytes()
    {
        var magic = SealFormat.MagicContainer.ToArray();
        Assert.Equal(8, magic.Length);
        Assert.Equal("NOVASEAL", System.Text.Encoding.ASCII.GetString(magic));
    }



    [Fact]
    public void WriteTo_ProducesTheDocumentedByteLayout()
    {
        var bytes = Bytes(flags: SealFormat.FlagFolderArchive, length: 0x0102030405060708UL);

        Assert.Equal(SealFormat.MagicContainer.ToArray(), bytes[..8].ToArray());
        Assert.Equal(1, bytes[8]);
        Assert.Equal(1, bytes[9]);
        Assert.Equal(SealFormat.FlagFolderArchive, bytes[10]);
        Assert.Equal(0, bytes[11]);
        Assert.Equal(IdA(), bytes[12..28].ToArray());

        var length = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(28, 8));
        Assert.Equal(0x0102030405060708UL, length);

        var sealedAt = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(36, 8));
        Assert.Equal(SealedAt, sealedAt);

        var chunk = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(44, 4));
        Assert.Equal(SealFormat.DefaultChunkSize, chunk);

        Assert.Equal(Slot(), bytes[48..112].ToArray());

        for (var i = 112; i < 128; i++) Assert.Equal(0, bytes[i]);
    }

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var original = Header(SealFormat.FlagFolderArchive, 123456789UL);

        Assert.True(SealContainerHeader.TryRead(Bytes(original), out var parsed, out var error));
        Assert.Equal(SealFormatError.None, error);
        Assert.True(parsed.IsInitialised);

        Assert.Equal(original.Version, parsed.Version);
        Assert.Equal(original.AlgoId, parsed.AlgoId);
        Assert.Equal(original.Flags, parsed.Flags);
        Assert.Equal(original.PlaintextLength, parsed.PlaintextLength);
        Assert.Equal(original.SealedAtUtc, parsed.SealedAtUtc);
        Assert.Equal(original.ChunkSize, parsed.ChunkSize);
        Assert.Equal(original.ItemId.ToArray(), parsed.ItemId.ToArray());
        Assert.Equal(original.KeySlot.ToArray(), parsed.KeySlot.ToArray());

        Assert.Equal(Bytes(original), Bytes(parsed));
    }

    [Fact]
    public void ZeroLength_IsALegalHeader()
    {
        var parsed = Read(Bytes(length: 0));
        Assert.Equal(0UL, parsed.PlaintextLength);
    }



    [Fact]
    public void FewerThan128Bytes_IsTooShort()
    {
        Assert.False(SealContainerHeader.TryRead(new byte[127], out var header, out var error));
        Assert.Equal(SealFormatError.TooShort, error);
        Assert.False(header.IsInitialised);
    }


    [Fact]
    public void TrailingBytes_AreIgnoredByTheHeaderParser()
    {
        var longer = new byte[SealFormat.HeaderSize + 1];
        Bytes().CopyTo(longer, 0);
        Assert.True(SealContainerHeader.TryRead(longer, out _, out var error));
        Assert.Equal(SealFormatError.None, error);
    }

    [Fact]
    public void WrongMagic_IsRefused()
    {
        var bytes = Bytes();
        bytes[0] = (byte)'X';
        AssertRefused(bytes, SealFormatError.Magic);
    }





    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(0)]
    public void UnsupportedVersion_IsReportedAsVersionNotCorruption(byte version)
    {
        var bytes = Bytes();
        bytes[SealFormat.VersionOffset] = version;
        AssertRefused(bytes, SealFormatError.VersionUnsupported);
    }

    [Fact]
    public void UnknownAlgorithm_IsRefused()
    {
        var bytes = Bytes();
        bytes[SealFormat.AlgoIdOffset] = 2;
        AssertRefused(bytes, SealFormatError.Algo);
    }

    [Fact]
    public void UndefinedFlagBits_AreRefused()
    {
        var bytes = Bytes();
        bytes[SealFormat.FlagsOffset] = 0x02;
        AssertRefused(bytes, SealFormatError.Flags);
    }

    [Fact]
    public void NonZeroReservedByte_IsRefused()
    {
        var bytes = Bytes();
        bytes[SealFormat.ReservedOffset] = 1;
        AssertRefused(bytes, SealFormatError.Reserved);
    }

    [Fact]
    public void NonZeroTrailerReservedRange_IsRefused()
    {
        foreach (var offset in new[] { 112, 119, 127 })
        {
            var bytes = Bytes();
            bytes[offset] = 0x7F;
            AssertRefused(bytes, SealFormatError.Reserved);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SealFormat.MinChunkSize - 1)]
    [InlineData(SealFormat.MaxChunkSize + 1)]
    public void ChunkSizeOutsideTheWindow_IsRefused(int chunkSize)
    {
        var bytes = Bytes();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(SealFormat.ChunkSizeOffset, 4), chunkSize);
        AssertRefused(bytes, SealFormatError.ChunkSize);
    }

    [Theory]
    [InlineData(3 * 1024 * 1024)]
    [InlineData(5 * 4096)]
    public void ChunkSizeThatIsNotAPowerOfTwo_IsRefused(int chunkSize)
    {
        var bytes = Bytes();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(SealFormat.ChunkSizeOffset, 4), chunkSize);
        AssertRefused(bytes, SealFormatError.ChunkSize);
    }

    [Fact]
    public void PlaintextLengthAboveSignedRange_IsRefused()
    {
        var bytes = Bytes();
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(SealFormat.PlaintextLengthOffset, 8), ulong.MaxValue);
        AssertRefused(bytes, SealFormatError.PlaintextLength);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(SealFormat.MinSealedAtUtc - 1)]
    [InlineData(SealFormat.MaxSealedAtUtc + 1)]
    public void TimestampOutsideTheWindow_IsRefused(long sealedAt)
    {
        var bytes = Bytes();
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(SealFormat.SealedAtUtcOffset, 8), sealedAt);
        AssertRefused(bytes, SealFormatError.SealedAtUtc);
    }



    [Fact]
    public void Create_RejectsProgrammingErrors()
    {
        Assert.Throws<ArgumentException>(() =>
            SealContainerHeader.Create(new byte[15], 0, SealedAt, SealFormat.DefaultChunkSize, Slot()));
        Assert.Throws<ArgumentException>(() =>
            SealContainerHeader.Create(IdA(), 0, SealedAt, SealFormat.DefaultChunkSize, new byte[63]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SealContainerHeader.Create(IdA(), 0, SealedAt, 0, Slot()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SealContainerHeader.Create(IdA(), 0, SealedAt, SealFormat.DefaultChunkSize, Slot(), flags: 0x02));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SealContainerHeader.Create(IdA(), 0, 0, SealFormat.DefaultChunkSize, Slot()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SealContainerHeader.Create(IdA(), 0, SealedAt, SealFormat.DefaultChunkSize, Slot(), flags: 0x03));
    }

    [Fact]
    public void WriteTo_RejectsShortDestination()
    {
        Assert.Throws<ArgumentException>(() => Header().WriteTo(new byte[127]));
    }


    [Fact]
    public void DefaultHeader_IsNotInitialised()
    {
        var header = default(SealContainerHeader);
        Assert.False(header.IsInitialised);
        Assert.True(header.ItemId.IsEmpty);
        Assert.True(header.KeySlot.IsEmpty);
        Assert.Throws<InvalidOperationException>(() => header.WriteTo(new byte[SealFormat.HeaderSize]));
    }





    [Fact]
    public void ParsedHeader_DoesNotAliasTheSourceBuffer()
    {
        var bytes = Bytes();
        Assert.True(SealContainerHeader.TryRead(bytes, out var parsed, out _));

        bytes[SealFormat.ItemIdOffset] = 0xFF;
        bytes[SealFormat.KeySlotOffset] = 0xFF;

        Assert.Equal(0, parsed.ItemId[0]);
        Assert.Equal(0xA0, parsed.KeySlot[0]);
    }



    private static SealContainerHeader Read(byte[] bytes)
    {
        Assert.True(SealContainerHeader.TryRead(bytes, out var header, out var error));
        Assert.Equal(SealFormatError.None, error);
        return header;
    }

    private static void AssertRefused(byte[] bytes, SealFormatError expected)
    {
        Assert.False(SealContainerHeader.TryRead(bytes, out var header, out var error));
        Assert.Equal(expected, error);
        Assert.False(header.IsInitialised);
    }
}
