namespace Novara.Services;


public enum SealCorruptReason
{

    HeaderInvalid,


    Truncated,


    BlockLength,


    Authentication,


    TrailingData,


    LengthMismatch,




    IndexFull,


    ContentDigest,



    IoUnavailable,
}





public sealed class SealCorruptException(SealCorruptReason reason)
    : Exception($"sealed container rejected: {reason}")
{
    public SealCorruptReason Reason { get; } = reason;
}





public sealed class SealVersionUnsupportedException(byte version)
    : Exception($"sealed container version {version} is not supported by this build")
{
    public byte Version { get; } = version;
}
