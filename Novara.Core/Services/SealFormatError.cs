namespace Novara.Services;









public enum SealFormatError
{

    None = 0,


    TooShort,


    Magic,


    VersionUnsupported,


    Algo,


    Flags,


    Reserved,


    ChunkSize,


    PlaintextLength,


    SealedAtUtc,







    Authentication,





    KdfMemory,


    KdfIterations,


    KdfParallelism,
}
