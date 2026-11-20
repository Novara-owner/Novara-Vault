namespace Novara.Models;


public enum SealTakeConflictPolicy
{

    Rename = 0,


    AutoNumber,


    Cancel,
}


public enum SealTakeStatus
{
    Taken = 0,


    Locked,


    ClockSuspected,


    ItemMissing,


    Conflict,


    TargetUnavailable,


    Cancelled,



    ExtractFailed,


    Busy,
}


public sealed record SealTakeRequest(
    string ItemId,
    string TargetDirectory,
    SealTakeConflictPolicy Policy = SealTakeConflictPolicy.AutoNumber,
    string? TargetName = null,
    bool ExtractAsArchive = false);





public sealed record SealTakeResult(
    string ItemId,
    SealTakeStatus Status,
    string? TargetPath = null,
    long? LockedUntilUtc = null,


    string? LeftoverPath = null)
{
    public bool Taken => Status == SealTakeStatus.Taken;
}
