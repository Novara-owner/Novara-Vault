namespace Novara.Services;


public enum SealPreflightStatus
{
    Ok = 0,


    Insufficient,


    Unreachable,
}




public readonly record struct SealPreflightResult(
    SealPreflightStatus Status,
    long Needed,
    long Available,
    long Shortfall)
{
    public bool Ok => Status == SealPreflightStatus.Ok;
}

















public static class SealPreflight
{

    public const int BlockOverheadBytes =
        SealFormat.BlockLengthPrefixSize + SealFormat.NonceSize + SealFormat.TagSize;


    public const long FolderSlackFloorBytes = 1024 * 1024;


    public const int FolderSlackPercent = 5;





    public static long Estimate(long plainBytes, bool isFolderPack = false, int chunkSize = SealFormat.DefaultChunkSize)
    {
        if (plainBytes < 0) throw new ArgumentOutOfRangeException(nameof(plainBytes));
        if (chunkSize < SealFormat.MinChunkSize || chunkSize > SealFormat.MaxChunkSize)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "chunk size is outside the format window");


        var blocks = plainBytes == 0 ? 0 : (plainBytes - 1) / chunkSize + 1;
        var total = SealFormat.HeaderSize + plainBytes + blocks * BlockOverheadBytes;

        if (isFolderPack)
            total += Math.Max(FolderSlackFloorBytes, plainBytes * FolderSlackPercent / 100);

        return total;
    }





    public static SealPreflightResult Check(string volumePath, long needed)
    {
        if (needed < 0) throw new ArgumentOutOfRangeException(nameof(needed));

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(volumePath));
            if (string.IsNullOrEmpty(root)) return new(SealPreflightStatus.Unreachable, needed, 0, needed);

            var drive = new DriveInfo(root);
            if (!drive.IsReady) return new(SealPreflightStatus.Unreachable, needed, 0, needed);


            var available = drive.AvailableFreeSpace;

            return available >= needed
                ? new SealPreflightResult(SealPreflightStatus.Ok, needed, available, 0)
                : new SealPreflightResult(SealPreflightStatus.Insufficient, needed, available, needed - available);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {

            return new SealPreflightResult(SealPreflightStatus.Unreachable, needed, 0, needed);
        }
    }


    public static SealPreflightResult CheckFor(string volumePath, long plainBytes, bool isFolderPack = false)
        => Check(volumePath, Estimate(plainBytes, isFolderPack));
}
