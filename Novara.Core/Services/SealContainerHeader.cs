using System.Buffers.Binary;

namespace Novara.Services;

















public readonly struct SealContainerHeader
{
    private readonly byte[]? _itemId;
    private readonly byte[]? _keySlot;


    public byte Version { get; }


    public byte AlgoId { get; }


    public byte Flags { get; }


    public ulong PlaintextLength { get; }


    public long SealedAtUtc { get; }


    public int ChunkSize { get; }





    public ReadOnlySpan<byte> ItemId
    {
        get
        {
            if (_itemId is null) return ReadOnlySpan<byte>.Empty;
            return _itemId;
        }
    }





    public ReadOnlySpan<byte> KeySlot
    {
        get
        {
            if (_keySlot is null) return ReadOnlySpan<byte>.Empty;
            return _keySlot;
        }
    }


    public bool IsInitialised => _itemId is not null && _keySlot is not null;

    private SealContainerHeader(
        byte version, byte algoId, byte flags,
        byte[] itemId, ulong plaintextLength, long sealedAtUtc, int chunkSize, byte[] keySlot)
    {
        Version = version;
        AlgoId = algoId;
        Flags = flags;
        _itemId = itemId;
        PlaintextLength = plaintextLength;
        SealedAtUtc = sealedAtUtc;
        ChunkSize = chunkSize;
        _keySlot = keySlot;
    }






    public static SealContainerHeader Create(
        ReadOnlySpan<byte> itemId,
        ulong plaintextLength,
        long sealedAtUtc,
        int chunkSize,
        ReadOnlySpan<byte> keySlot,
        byte flags = 0)
    {
        if (itemId.Length != SealFormat.ItemIdSize)
            throw new ArgumentException($"item id must be {SealFormat.ItemIdSize} bytes", nameof(itemId));
        if (keySlot.Length != SealFormat.KeySlotSize)
            throw new ArgumentException($"key slot must be {SealFormat.KeySlotSize} bytes", nameof(keySlot));
        if (!IsSupportedChunkSize(chunkSize))
            throw new ArgumentOutOfRangeException(nameof(chunkSize),
                $"chunk size must be a power of two in [{SealFormat.MinChunkSize}, {SealFormat.MaxChunkSize}]");
        if ((flags & ~SealFormat.FlagFolderArchive) != 0)
            throw new ArgumentOutOfRangeException(nameof(flags), "only the folder-archive flag is defined in v1");
        if (plaintextLength > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(plaintextLength), "length must fit a signed 64-bit value");
        if (sealedAtUtc < SealFormat.MinSealedAtUtc || sealedAtUtc > SealFormat.MaxSealedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(sealedAtUtc), "timestamp is outside the representable window");

        return new SealContainerHeader(
            SealFormat.ContainerVersion, SealFormat.AlgoAes256Gcm, flags,
            itemId.ToArray(), plaintextLength, sealedAtUtc, chunkSize, keySlot.ToArray());
    }


    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < SealFormat.HeaderSize)
            throw new ArgumentException($"destination must hold at least {SealFormat.HeaderSize} bytes", nameof(destination));
        if (!IsInitialised)
            throw new InvalidOperationException("header was never initialised");

        var span = destination[..SealFormat.HeaderSize];
        span.Clear();

        SealFormat.MagicContainer.CopyTo(span[SealFormat.MagicOffset..]);
        span[SealFormat.VersionOffset] = Version;
        span[SealFormat.AlgoIdOffset] = AlgoId;
        span[SealFormat.FlagsOffset] = Flags;
        span[SealFormat.ReservedOffset] = 0;
        ItemId.CopyTo(span[SealFormat.ItemIdOffset..]);
        BinaryPrimitives.WriteUInt64LittleEndian(span[SealFormat.PlaintextLengthOffset..], PlaintextLength);
        BinaryPrimitives.WriteInt64LittleEndian(span[SealFormat.SealedAtUtcOffset..], SealedAtUtc);
        BinaryPrimitives.WriteInt32LittleEndian(span[SealFormat.ChunkSizeOffset..], ChunkSize);
        KeySlot.CopyTo(span[SealFormat.KeySlotOffset..]);
    }





    public static bool TryRead(ReadOnlySpan<byte> source, out SealContainerHeader header, out SealFormatError error)
    {
        header = default;

        if (source.Length < SealFormat.HeaderSize)
        {
            error = SealFormatError.TooShort;
            return false;
        }

        var span = source[..SealFormat.HeaderSize];
        var magic = SealFormat.MagicContainer;

        if (!span.Slice(SealFormat.MagicOffset, magic.Length).SequenceEqual(magic))
        {
            error = SealFormatError.Magic;
            return false;
        }

        var version = span[SealFormat.VersionOffset];
        if (version == 0 || version > SealFormat.ContainerVersionMax)
        {
            error = SealFormatError.VersionUnsupported;
            return false;
        }

        var algoId = span[SealFormat.AlgoIdOffset];
        if (algoId != SealFormat.AlgoAes256Gcm)
        {
            error = SealFormatError.Algo;
            return false;
        }

        var flags = span[SealFormat.FlagsOffset];
        if ((flags & ~SealFormat.FlagFolderArchive) != 0)
        {
            error = SealFormatError.Flags;
            return false;
        }

        if (span[SealFormat.ReservedOffset] != 0)
        {
            error = SealFormatError.Reserved;
            return false;
        }

        for (var i = 0; i < SealFormat.TrailerReservedSize; i++)
        {
            if (span[SealFormat.TrailerReservedOffset + i] != 0)
            {
                error = SealFormatError.Reserved;
                return false;
            }
        }

        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(span[SealFormat.ChunkSizeOffset..]);
        if (!IsSupportedChunkSize(chunkSize))
        {
            error = SealFormatError.ChunkSize;
            return false;
        }

        var plaintextLength = BinaryPrimitives.ReadUInt64LittleEndian(span[SealFormat.PlaintextLengthOffset..]);
        if (plaintextLength > long.MaxValue)
        {
            error = SealFormatError.PlaintextLength;
            return false;
        }

        var sealedAtUtc = BinaryPrimitives.ReadInt64LittleEndian(span[SealFormat.SealedAtUtcOffset..]);
        if (sealedAtUtc < SealFormat.MinSealedAtUtc || sealedAtUtc > SealFormat.MaxSealedAtUtc)
        {
            error = SealFormatError.SealedAtUtc;
            return false;
        }

        header = new SealContainerHeader(
            version, algoId, flags,
            span.Slice(SealFormat.ItemIdOffset, SealFormat.ItemIdSize).ToArray(),
            plaintextLength, sealedAtUtc, chunkSize,
            span.Slice(SealFormat.KeySlotOffset, SealFormat.KeySlotSize).ToArray());
        error = SealFormatError.None;
        return true;
    }

    private static bool IsSupportedChunkSize(int value)
        => value >= SealFormat.MinChunkSize
           && value <= SealFormat.MaxChunkSize
           && (value & (value - 1)) == 0;
}
