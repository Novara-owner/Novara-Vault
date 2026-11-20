using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealPreflightTests
{
    private const long OneMiB = 1024 * 1024;



    [Fact]
    public void ZeroBytes_CostsJustTheHeader()
    {
        Assert.Equal(SealFormat.HeaderSize, SealPreflight.Estimate(0));
    }

    [Fact]
    public void ExactlyOneChunk_HasOneBlockOfOverhead()
    {
        Assert.Equal(SealFormat.HeaderSize + OneMiB + SealPreflight.BlockOverheadBytes,
            SealPreflight.Estimate(OneMiB));
    }

    [Fact]
    public void OneByteOverAChunk_HasTwo()
    {
        Assert.Equal(SealFormat.HeaderSize + OneMiB + 1 + 2 * SealPreflight.BlockOverheadBytes,
            SealPreflight.Estimate(OneMiB + 1));
    }

    [Fact]
    public void BlockOverheadIsThirtyTwoBytes()
    {


        Assert.Equal(32, SealPreflight.BlockOverheadBytes);
    }

    [Fact]
    public void AFolderEstimate_CarriesFivePercent()
    {
        var oneGiB = 1024L * 1024 * 1024;

        var plain = SealPreflight.Estimate(oneGiB);
        var packed = SealPreflight.Estimate(oneGiB, isFolderPack: true);

        Assert.Equal(oneGiB * SealPreflight.FolderSlackPercent / 100, packed - plain);
    }

    [Fact]
    public void ASmallFolder_StillGetsTheOneMiBFloor()
    {
        var tiny = 1024L;

        var plain = SealPreflight.Estimate(tiny);
        var packed = SealPreflight.Estimate(tiny, isFolderPack: true);

        Assert.Equal(SealPreflight.FolderSlackFloorBytes, packed - plain);
    }

    [Fact]
    public void ANegativeSize_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SealPreflight.Estimate(-1));
    }







    [Fact]
    public async Task TheEstimate_IsNeverBelowWhatTheWriterProduces()
    {
        var plain = new byte[3 * OneMiB];
        var vmk = Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)i).ToArray();
        var contentKey = Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x80 + i)).ToArray();
        var itemId = Enumerable.Range(0, SealFormat.ItemIdSize).Select(i => (byte)(0x40 + i)).ToArray();

        var header = SealContainerWriter.PrepareHeader(
            itemId, (ulong)plain.Length, 1_790_000_000, SealFormat.DefaultChunkSize, vmk, contentKey);

        var output = new MemoryStream();
        await SealContainerWriter.WriteAsync(new MemoryStream(plain), header, contentKey, output);

        var estimate = SealPreflight.Estimate(plain.Length);

        Assert.True(estimate >= output.Length, $"estimate {estimate} is below the real container {output.Length}");
        Assert.Equal(output.Length, estimate);
    }



    [Fact]
    public void TheTemporaryVolume_HasRoomForAKilobyte()
    {
        var result = SealPreflight.Check(Path.GetTempPath(), 1024);

        Assert.True(result.Ok, $"status {result.Status}");
        Assert.Equal(SealPreflightStatus.Ok, result.Status);
        Assert.True(result.Available > 0);
        Assert.Equal(0, result.Shortfall);
    }

    [Fact]
    public void AnImpossibleRequirement_ReportsTheShortfallAsAPositiveNumber()
    {
        var result = SealPreflight.Check(Path.GetTempPath(), long.MaxValue);

        Assert.Equal(SealPreflightStatus.Insufficient, result.Status);
        Assert.False(result.Ok);
        Assert.True(result.Shortfall > 0, "the shortfall must be positive so the UI can show it");
        Assert.Equal(long.MaxValue - result.Available, result.Shortfall);
    }






    [Fact]
    public void TheCheckReadsTheVolumeOfTheGivenPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "some", "nested", "file.txt");
        var root = Path.GetPathRoot(Path.GetFullPath(path))!;

        var expected = new DriveInfo(root).AvailableFreeSpace;
        var result = SealPreflight.Check(path, 1);

        Assert.Equal(SealPreflightStatus.Ok, result.Status);
        Assert.Equal(expected, result.Available);
    }

    [Fact]
    public void AnEmptyPath_IsUnreachableRatherThanAnException()
    {
        var result = SealPreflight.Check("", 1024);

        Assert.Equal(SealPreflightStatus.Unreachable, result.Status);
        Assert.Equal(1024, result.Needed);
        Assert.Equal(0, result.Available);
    }

    [Fact]
    public void CheckFor_CombinesTheTwoSteps()
    {
        var result = SealPreflight.CheckFor(Path.GetTempPath(), OneMiB, isFolderPack: true);

        Assert.Equal(SealPreflightStatus.Ok, result.Status);
        Assert.Equal(SealPreflight.Estimate(OneMiB, isFolderPack: true), result.Needed);
    }
}
