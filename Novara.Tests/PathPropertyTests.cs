using Novara.Services;
using System.Globalization;
using Xunit;

namespace Novara.Tests;


[Collection("CoreSequential")]
public class PathPropertyTests : IDisposable
{
    private readonly string _root;

    public PathPropertyTests()
    {
        _root = Directory.CreateTempSubdirectory("novara-pathprop-").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }



    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(862_208, "842 KB")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(1_288_490_188, "1.2 GB")]
    [InlineData(1_979_120_929_996_800, "1800.0 TB")]
    public void FormatSize_1024Base(long bytes, string expected)
        => Assert.Equal(expected, PathPropertyFormatter.FormatSize(bytes, CultureInfo.InvariantCulture));

    [Fact]
    public void FormatCount_ThousandsSeparators()
    {
        Assert.Equal("1,234", PathPropertyFormatter.FormatCount(1234, CultureInfo.InvariantCulture));
        Assert.Equal("0", PathPropertyFormatter.FormatCount(0, CultureInfo.InvariantCulture));
        Assert.Equal("1,288,490,188", PathPropertyFormatter.FormatCount(1_288_490_188, CultureInfo.InvariantCulture));
    }



    [Fact]
    public void Read_File_ReadyWithSingleCountAndNullFolders()
    {
        var p = Path.Combine(_root, "a.txt");
        File.WriteAllText(p, new string('x', 5325));
        var r = PathPropertyReader.Read(p, PathPropertyReader.DefaultTimeout, null, CancellationToken.None);
        Assert.Equal(PathPropertyState.Ready, r.State);
        Assert.Equal(5325, r.Bytes);
        Assert.Equal(1, r.Files);
        Assert.Null(r.Folders);
        Assert.NotNull(r.ModifiedAtUtc);
    }



    [Fact]
    public void Read_Folder_RecursiveCountsAndLogicalSize()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_root, "sub"));
        var sub2 = Directory.CreateDirectory(Path.Combine(sub.FullName, "deep"));
        File.WriteAllText(Path.Combine(_root, "f1.bin"), new string('a', 1000));
        File.WriteAllText(Path.Combine(sub.FullName, "f2.bin"), new string('b', 2000));
        File.WriteAllText(Path.Combine(sub2.FullName, "f3.bin"), new string('c', 44));
        Directory.CreateDirectory(Path.Combine(_root, "empty-dir"));

        var r = PathPropertyReader.Read(_root, PathPropertyReader.DefaultTimeout, null, CancellationToken.None);
        Assert.Equal(PathPropertyState.Ready, r.State);
        Assert.Equal(3044, r.Bytes);
        Assert.Equal(3, r.Files);
        Assert.Equal(3, r.Folders);
    }

    [Fact]
    public void Read_Folder_ProgressPushed()
    {
        var d = Directory.CreateDirectory(Path.Combine(_root, "many"));
        for (int i = 0; i < 600; i++) File.WriteAllText(Path.Combine(d.FullName, $"f{i}.txt"), "x");
        var pushes = new List<PathPropertyProgress>();
        var r = PathPropertyReader.Read(d.FullName, PathPropertyReader.DefaultTimeout, pushes.Add, CancellationToken.None);
        Assert.Equal(PathPropertyState.Ready, r.State);
        Assert.Equal(600, r.Files);
        Assert.True(pushes.Count > 0, "600 条目应至少推一次增量快照");
        var last = pushes[^1];
        Assert.True(last.Files <= 600);
    }



    [Fact]
    public void Read_MissingPath_Invalid()
    {
        var r = PathPropertyReader.Read(Path.Combine(_root, "no-such"), PathPropertyReader.DefaultTimeout, null, CancellationToken.None);
        Assert.Equal(PathPropertyState.Invalid, r.State);
        Assert.Null(r.Bytes);
        Assert.Null(r.Files);
        Assert.Null(r.Folders);
    }

    [Fact]
    public void Read_ExternalCancel_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            PathPropertyReader.Read(_root, PathPropertyReader.DefaultTimeout, null, cts.Token));
    }

    [Fact]
    public void Read_SteadyProgress_Completes_BeyondStallThreshold()
    {



        var d = Directory.CreateDirectory(Path.Combine(_root, "big"));
        for (int i = 0; i < 5000; i++) File.WriteAllText(Path.Combine(d.FullName, $"f{i}.txt"), "x");
        var r = PathPropertyReader.Read(d.FullName, TimeSpan.FromMilliseconds(30), null, CancellationToken.None);
        Assert.Equal(PathPropertyState.Ready, r.State);
        Assert.Equal(5000, r.Files);
    }

    [Fact]
    public void Read_Stall_BeforeFirstEntry_Unavailable()
    {


        var d = Directory.CreateDirectory(Path.Combine(_root, "empty"));
        var r = PathPropertyReader.Read(d.FullName, TimeSpan.FromMilliseconds(1), null, CancellationToken.None);
        Assert.Equal(PathPropertyState.Ready, r.State);
    }

    [Fact]
    public void Read_ReturnDoesNotCrashOnLateWatchdogCallback()
    {





        var d = Directory.CreateDirectory(Path.Combine(_root, "race"));
        File.WriteAllText(Path.Combine(d.FullName, "a.txt"), "x");
        for (int i = 0; i < 300; i++)
        {
            var r = PathPropertyReader.Read(d.FullName, TimeSpan.FromMilliseconds(1), null, CancellationToken.None);
            Assert.True(r.State is PathPropertyState.Ready or PathPropertyState.Unavailable, $"iteration {i}: {r.State}");
        }
    }



    [Fact]
    public void NormalizeKey_TrailingSeparatorsFold()
    {
        var a = PathPropertyReader.NormalizeKey(@"C:\dir\sub");
        var b = PathPropertyReader.NormalizeKey(@"C:\dir\sub\");
        var c = PathPropertyReader.NormalizeKey(@"C:\dir\sub\\");
        Assert.NotNull(a);
        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.Equal(@"C:\", PathPropertyReader.NormalizeKey(@"C:\"));
        Assert.Equal(@"C:\", PathPropertyReader.NormalizeKey(@"C:"));
        Assert.Null(PathPropertyReader.NormalizeKey(""));
        Assert.Null(PathPropertyReader.NormalizeKey(null));
    }



    [Fact]
    public void PathPropertyRefreshMinutes_NotInRoamingSettings()
    {
        var src = new Novara.Models.AppSettings { PathPropertyRefreshMinutes = 15 };
        var roaming = SyncFieldPolicy.BuildRoamingSettings(src);
        Assert.Equal(0, roaming.PathPropertyRefreshMinutes);
    }
}
