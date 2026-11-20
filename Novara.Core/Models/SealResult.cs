using Novara.Services;

namespace Novara.Models;


public enum SealSealError
{
    None = 0,


    Rejected,


    InvalidRequest,


    SourceMissing,


    NotEnoughSpace,


    VolumeUnreachable,


    PackFailed,


    WriteFailed,


    VerifyFailed,


    ItemIdCollision,


    IndexFailed,


    SourceDeleteFailed,


    Cancelled,
}








public sealed record SealRequest(
    string SourcePath,
    string? DisplayName = null,
    string Kind = SealedItemKind.Normal,
    long? ExpirySeconds = null,
    string? Note = null);





public readonly record struct SealProgress(int Current, int Total, string ItemName);










public sealed record SealResult(string ItemId, bool SourceDeleted, bool IndexUpdated, SealSealError Error = SealSealError.None)
{

    public SealRejectReason? Reject { get; init; }


    public bool Committed => IndexUpdated && Error is SealSealError.None or SealSealError.SourceDeleteFailed;
}
