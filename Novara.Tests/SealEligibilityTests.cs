using System.Diagnostics;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealEligibilityTests
{
    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "novara-seal-elig-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Entry(string name) => System.IO.Path.Combine(Root, name);

        public void Dispose()
        {
            try
            {


                foreach (var directory in Directory.GetDirectories(Root, "*", SearchOption.TopDirectoryOnly))
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                        Directory.Delete(directory, recursive: false);
                }

                foreach (var file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);

                Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {

            }
        }
    }

    private static SealSpacePaths Space(TempDir dir) => new(System.IO.Path.Combine(dir.Root, "space"));



    [Fact]
    public void AnOrdinaryFile_IsAccepted()
    {
        using var dir = new TempDir();
        var file = dir.Entry("note.txt");
        File.WriteAllText(file, "hello");

        Assert.Equal((true, SealRejectReason.None), SealEligibility.Evaluate(file, Space(dir)));
    }

    [Fact]
    public void AnEmptyFile_IsAccepted()
    {
        using var dir = new TempDir();
        var file = dir.Entry("empty.bin");
        File.WriteAllBytes(file, []);

        Assert.Equal((true, SealRejectReason.None), SealEligibility.Evaluate(file, Space(dir)));
    }

    [Fact]
    public void AFolder_IsAccepted()
    {
        using var dir = new TempDir();
        var folder = dir.Entry("photos");
        Directory.CreateDirectory(folder);

        Assert.Equal((true, SealRejectReason.None), SealEligibility.Evaluate(folder, Space(dir)));
    }





    [Fact]
    public void AnExtendedLengthPath_IsNotTreatedAsADevice()
    {
        using var dir = new TempDir();
        var file = dir.Entry("plain.txt");
        File.WriteAllText(file, "x");

        var extended = @"\\?\" + file;

        Assert.False(SealEligibility.IsDevicePath(extended));
        Assert.Equal((true, SealRejectReason.None), SealEligibility.Evaluate(extended, Space(dir)));
    }








    [Fact]
    public void AJunction_IsRefused()
    {
        using var dir = new TempDir();
        var target = dir.Entry("target");
        Directory.CreateDirectory(target);
        File.WriteAllText(System.IO.Path.Combine(target, "inside.txt"), "x");

        var link = dir.Entry("link");
        CreateJunction(link, target);

        Assert.True(Directory.Exists(link), "the junction was not created; the test would otherwise prove nothing");

        var (ok, reason) = SealEligibility.Evaluate(link, Space(dir));
        Assert.False(ok);
        Assert.Equal(SealRejectReason.ReparsePoint, reason);
    }

    [Theory]
    [InlineData("nul")]
    [InlineData("con.txt")]
    [InlineData("COM1")]
    [InlineData("lpt9.log")]
    public void ReservedNames_AreRefused(string name)
    {
        using var dir = new TempDir();
        Assert.Equal(SealRejectReason.ReservedName, SealEligibility.Evaluate(dir.Entry(name), Space(dir)).Reason);
    }

    [Fact]
    public void APathInsideTheSpaceRoot_IsRefused()
    {
        using var dir = new TempDir();
        var space = Space(dir);
        Directory.CreateDirectory(space.Items);

        Assert.Equal(SealRejectReason.InsideSealRoot, SealEligibility.Evaluate(space.Root, space).Reason);
        Assert.Equal(SealRejectReason.InsideSealRoot,
            SealEligibility.Evaluate(space.ItemPath("00ff"), space).Reason);
    }


    [Fact]
    public void AFileReachedThroughAJunction_IsRecognisedAsInsideTheSpace()
    {
        using var dir = new TempDir();
        var space = Space(dir);
        Directory.CreateDirectory(space.Items);
        var inside = space.ItemPath("00ff");
        File.WriteAllText(inside, "x");

        var link = dir.Entry("space-link");
        CreateJunction(link, space.Root);
        Assert.True(Directory.Exists(link), "the junction was not created; the test would otherwise prove nothing");

        var throughLink = System.IO.Path.Combine(link, "items", "00ff.novaseal");
        Assert.True(File.Exists(throughLink));
        Assert.True(space.Contains(throughLink));
        Assert.Equal(SealRejectReason.InsideSealRoot, SealEligibility.Evaluate(throughLink, space).Reason);
    }

    [Fact]
    public void AFolderThatContainsTheSpaceRoot_IsRefused()
    {
        using var dir = new TempDir();
        var space = Space(dir);
        Directory.CreateDirectory(space.Root);
        Directory.CreateDirectory(space.Items);



        Assert.Equal(SealRejectReason.ContainsSealRoot, SealEligibility.Evaluate(dir.Root, space).Reason);
        Assert.Equal(SealRejectReason.ContainsSealRoot,
            SealEligibility.Evaluate(Path.GetDirectoryName(dir.Root.TrimEnd(Path.DirectorySeparatorChar))!, space).Reason);
    }

    [Fact]
    public void ABusyFile_IsRefused()
    {
        using var dir = new TempDir();
        var file = dir.Entry("busy.bin");
        File.WriteAllBytes(file, new byte[16]);

        using (var holder = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var (ok, reason) = SealEligibility.Evaluate(file, Space(dir));
            Assert.False(ok);
            Assert.Equal(SealRejectReason.InUse, reason);
        }


        Assert.True(SealEligibility.Evaluate(file, Space(dir)).Ok);
    }

    [Fact]
    public void AReadOnlyFile_IsRefused()
    {
        using var dir = new TempDir();
        var file = dir.Entry("readonly.txt");
        File.WriteAllText(file, "x");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        var (ok, reason) = SealEligibility.Evaluate(file, Space(dir));
        Assert.False(ok);
        Assert.Equal(SealRejectReason.ReadOnly, reason);
    }

    [Fact]
    public void AMissingFile_IsReportedAsNotFound()
    {
        using var dir = new TempDir();
        Assert.Equal(SealRejectReason.NotFound,
            SealEligibility.Evaluate(dir.Entry("nope.txt"), Space(dir)).Reason);
    }

    [Fact]
    public void AnEmptyPath_IsRefused()
    {
        using var dir = new TempDir();
        Assert.Equal(SealRejectReason.NotFound, SealEligibility.Evaluate("", Space(dir)).Reason);
    }


    [Fact]
    public void APathBeyondTheWindowsCeiling_IsRefused()
    {
        using var dir = new TempDir();
        var absurd = @"C:\" + new string('a', SealEligibility.MaxPathLength);

        Assert.Equal(SealRejectReason.PathTooLong, SealEligibility.Evaluate(absurd, Space(dir)).Reason);
    }

    [Fact]
    public void ADeviceNamespacePath_IsRefused()
    {
        using var dir = new TempDir();
        Assert.Equal(SealRejectReason.PipeOrDevice,
            SealEligibility.Evaluate(@"\\.\pipe\some-pipe", Space(dir)).Reason);
    }







    [Fact]
    public void Refusals_LeaveTheDirectoryUntouched()
    {
        using var dir = new TempDir();
        var space = Space(dir);
        var file = dir.Entry("taken.bin");
        File.WriteAllBytes(file, new byte[8]);
        var before = string.Join("|", Directory.GetFileSystemEntries(dir.Root).OrderBy(p => p, StringComparer.Ordinal));

        using (var holder = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            SealEligibility.Evaluate(file, space);
        }

        SealEligibility.Evaluate(dir.Entry("nul"), space);
        SealEligibility.Evaluate(space.Root, space);
        SealEligibility.Evaluate(dir.Entry("gone"), space);

        var after = string.Join("|", Directory.GetFileSystemEntries(dir.Root).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal(before, after);
    }







    [Theory]
    [InlineData(0x400000, true)]
    [InlineData(0x1000, true)]
    [InlineData(0x40000, true)]
    [InlineData(0x400000 | 0x20, true)]
    [InlineData(0x20, false)]
    [InlineData(0x10, false)]
    public void CloudPlaceholderBits_AreDetected(int rawAttributes, bool expected)
    {
        Assert.Equal(expected, SealEligibility.HasCloudPlaceholder((FileAttributes)rawAttributes));
    }

    [Theory]
    [InlineData("plain.txt", false)]
    [InlineData("console.txt", false)]
    [InlineData("nul", true)]
    [InlineData("NUL.txt", true)]
    [InlineData("com9.backup", true)]
    [InlineData(@"C:\folder\aux", true)]
    public void ReservedNameDetection(string path, bool expected)
    {
        Assert.Equal(expected, SealEligibility.IsReservedName(path));
    }







    [Fact]
    public void AReadOnlyFolder_IsStillAccepted()
    {
        using var dir = new TempDir();
        var folder = dir.Entry("customised");
        Directory.CreateDirectory(folder);

        var info = new DirectoryInfo(folder);
        info.Attributes |= FileAttributes.ReadOnly;

        var (ok, reason) = SealEligibility.Evaluate(folder, Space(dir));

        Assert.True(ok, $"a folder with the cosmetic ReadOnly bit was refused as {reason}");
        Assert.Equal(SealRejectReason.None, reason);
    }








    [Fact]
    public void AFileTooLargeForFat32_IsRefused()
    {
        using var dir = new TempDir();
        var file = dir.Entry("huge.bin");
        File.WriteAllBytes(file, new byte[4]);

        var (ok, reason) = SealEligibility.EvaluateCore(
            file, Space(dir),
            _ => 5L * 1024 * 1024 * 1024,
            _ => "FAT32");

        Assert.False(ok);
        Assert.Equal(SealRejectReason.Fat32TooLarge, reason);
    }

    [Fact]
    public void TheFat32CeilingAppliesToTheContainer_NotTheSource()
    {
        using var dir = new TempDir();
        var file = dir.Entry("edge.bin");
        File.WriteAllBytes(file, new byte[4]);




        var containerOverhead = SealPreflight.Estimate(SealEligibility.Fat32MaxFileSize, false)
                                - SealEligibility.Fat32MaxFileSize;
        var atCeiling = SealEligibility.Fat32MaxFileSize - containerOverhead;

        var (ok, reason) = SealEligibility.EvaluateCore(
            file, Space(dir), _ => atCeiling, _ => "FAT32");
        Assert.True(ok);
        Assert.Equal(SealRejectReason.None, reason);


        var (over, overReason) = SealEligibility.EvaluateCore(
            file, Space(dir), _ => SealEligibility.Fat32MaxFileSize, _ => "FAT32");
        Assert.False(over);
        Assert.Equal(SealRejectReason.Fat32TooLarge, overReason);
    }

    [Fact]
    public void AHugeFileOnNtfs_IsAccepted()
    {
        using var dir = new TempDir();
        var file = dir.Entry("huge.bin");
        File.WriteAllBytes(file, new byte[4]);

        var (ok, reason) = SealEligibility.EvaluateCore(
            file, Space(dir),
            _ => 5L * 1024 * 1024 * 1024,
            _ => "NTFS");

        Assert.True(ok, $"a big file on NTFS was refused as {reason}");
    }


    [Fact]
    public void TheFat32CeilingIsCheckedAgainstTheSpaceRoot()
    {
        using var dir = new TempDir();
        var file = dir.Entry("huge.bin");
        File.WriteAllBytes(file, new byte[4]);
        var space = Space(dir);

        var (ok, reason) = SealEligibility.EvaluateCore(
            file, space,
            _ => 5L * 1024 * 1024 * 1024,
            path => string.Equals(path, space.Root, StringComparison.OrdinalIgnoreCase) ? "FAT32" : "NTFS");

        Assert.False(ok);
        Assert.Equal(SealRejectReason.Fat32TooLarge, reason);
    }


    [Fact]
    public void AFat32SourceVolumeDoesNotBlockALargeFile()
    {
        using var dir = new TempDir();
        var file = dir.Entry("huge.bin");
        File.WriteAllBytes(file, new byte[4]);

        var (ok, reason) = SealEligibility.EvaluateCore(
            file, Space(dir),
            _ => 5L * 1024 * 1024 * 1024,
            path => string.Equals(path, file, StringComparison.OrdinalIgnoreCase) ? "FAT32" : "NTFS");

        Assert.True(ok, $"the source volume's format wrongly refused the seal as {reason}");
    }

    [Fact]
    public void AnUnreadableSize_IsReportedAsUnreachable()
    {
        using var dir = new TempDir();
        var file = dir.Entry("vanished.bin");
        File.WriteAllBytes(file, new byte[4]);

        var (ok, reason) = SealEligibility.EvaluateCore(file, Space(dir), _ => null, _ => null);

        Assert.False(ok);
        Assert.Equal(SealRejectReason.Unreachable, reason);
    }






    [Fact]
    public void ADeniedFile_IsRefused()
    {
        using var dir = new TempDir();
        var locked = dir.Entry("locked");
        Directory.CreateDirectory(locked);

        var file = System.IO.Path.Combine(locked, "inside.txt");
        File.WriteAllText(file, "x");

        var user = Environment.UserName;
        var applied = RunIcacls($"\"{locked}\" /deny \"{user}:(OI)(CI)(RX)\"");

        Assert.True(applied, "icacls could not apply a deny ACE; the environment changed");

        try
        {
            var (ok, reason) = SealEligibility.Evaluate(file, Space(dir));
            Assert.False(ok);
            Assert.Equal(SealRejectReason.Denied, reason);
        }
        finally
        {
            RunIcacls($"\"{locked}\" /remove:d \"{user}\"");
        }
    }



    private static bool RunIcacls(string arguments)
    {
        var startInfo = new ProcessStartInfo("icacls.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo);
        process?.WaitForExit(5000);
        return process?.ExitCode == 0;
    }

    private static void CreateJunction(string link, string target)
    {
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo);
        process?.WaitForExit(5000);
    }
}
