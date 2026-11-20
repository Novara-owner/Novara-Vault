namespace Novara.Services;











public static class SealFormat
{



    public static ReadOnlySpan<byte> MagicContainer => "NOVASEAL"u8;


    public const byte ContainerVersion = 1;






    public const byte ContainerVersionMax = 1;


    public const byte AlgoAes256Gcm = 1;


    public const byte FlagFolderArchive = 0x01;






    public const int HeaderSize = 128;


    public const int AuthenticatedPrefixSize = 48;

    public const int MagicOffset = 0;
    public const int VersionOffset = 8;
    public const int AlgoIdOffset = 9;
    public const int FlagsOffset = 10;
    public const int ReservedOffset = 11;
    public const int ItemIdOffset = 12;
    public const int PlaintextLengthOffset = 28;
    public const int SealedAtUtcOffset = 36;
    public const int ChunkSizeOffset = 44;
    public const int KeySlotOffset = 48;
    public const int KeySlotSize = 64;
    public const int TrailerReservedOffset = 112;
    public const int TrailerReservedSize = 16;

    public const int ItemIdSize = 16;




    public const int NonceSize = 12;


    public const int TagSize = 16;


    public const int KeySize = 32;


    public const int BlockLengthPrefixSize = 4;


    public const int BlockAadSize = ItemIdSize + 4 + 1;




    public const int DefaultChunkSize = 1024 * 1024;

    public const int MinChunkSize = 4096;
    public const int MaxChunkSize = 4 * 1024 * 1024;




    public const long MinSealedAtUtc = 946_684_799;


    public const long MaxSealedAtUtc = 253_402_300_799;
}
