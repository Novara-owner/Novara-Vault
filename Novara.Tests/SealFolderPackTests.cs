using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealFolderPackTests
{
    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "novara-seal-pack-" + Guid.NewGuid().ToString("N"));
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

                Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {

            }
        }
    }







    [Fact]
    public void PackThenExtract_RestoresTheFolderExactly()
    {
        using var dir = new TempDir();

        var source = dir.Entry("source");
        Directory.CreateDirectory(System.IO.Path.Combine(source, "a", "b"));
        Directory.CreateDirectory(System.IO.Path.Combine(source, "empty"));
        File.WriteAllText(System.IO.Path.Combine(source, "a", "b", "deep.txt"), "deep");
        File.WriteAllText(System.IO.Path.Combine(source, "中文名.txt"), "中文内容");
        File.WriteAllBytes(System.IO.Path.Combine(source, "zero.bin"), []);

        var zip = dir.Entry("pack.zip");
        var packed = SealFolderPack.Pack(source, zip);

        var restored = dir.Entry("restored");
        SealFolderPack.Extract(zip, restored);

        Assert.Equal(Snapshot(source), Snapshot(restored));
        Assert.True(Directory.Exists(System.IO.Path.Combine(restored, "empty")), "the empty directory was lost");

        Assert.Equal(6, packed.EntryCount);
    }

    [Fact]
    public void EntryCount_CountsFilesAndDirectories()
    {
        using var dir = new TempDir();

        var source = dir.Entry("two-files-three-dirs");
        Directory.CreateDirectory(System.IO.Path.Combine(source, "x", "y"));
        Directory.CreateDirectory(System.IO.Path.Combine(source, "z"));
        File.WriteAllText(System.IO.Path.Combine(source, "x", "one.txt"), "1");
        File.WriteAllText(System.IO.Path.Combine(source, "two.txt"), "22");

        var packed = SealFolderPack.Pack(source, dir.Entry("count.zip"));

        Assert.Equal(5, packed.EntryCount);
        Assert.Equal(3, packed.OriginalTotalBytes);
    }

    [Fact]
    public void AnEmptyFolder_ProducesAnEmptyArchive()
    {
        using var dir = new TempDir();
        var source = dir.Entry("empty-root");
        Directory.CreateDirectory(source);

        var packed = SealFolderPack.Pack(source, dir.Entry("empty.zip"));

        Assert.Equal(0, packed.EntryCount);
        Assert.Equal(0, packed.OriginalTotalBytes);

        var restored = dir.Entry("restored");
        SealFolderPack.Extract(dir.Entry("empty.zip"), restored);
        Assert.Empty(Directory.GetFileSystemEntries(restored));
    }







    [Fact]
    public void A64MiBFile_RoundTripsWithTheRightSize()
    {
        using var dir = new TempDir();

        var source = dir.Entry("big");
        Directory.CreateDirectory(source);

        const int megabytes = 64;
        using (var stream = File.Create(System.IO.Path.Combine(source, "big.bin")))
        {
            var buffer = new byte[1024 * 1024];
            for (var i = 0; i < megabytes; i++) stream.Write(buffer);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var packed = SealFolderPack.Pack(source, dir.Entry("big.zip"));
        Assert.Equal((long)megabytes * 1024 * 1024, packed.OriginalTotalBytes);
        Assert.Equal(1, packed.EntryCount);

        var restored = dir.Entry("restored");
        SealFolderPack.Extract(dir.Entry("big.zip"), restored);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var after = GC.GetTotalMemory(forceFullCollection: true);



        Assert.True(after - before < 16L * 1024 * 1024,
            $"surviving memory grew by {(after - before) / 1024 / 1024} MiB across a 64 MiB round trip");

        var length = new FileInfo(System.IO.Path.Combine(restored, "big.bin")).Length;
        Assert.Equal((long)megabytes * 1024 * 1024, length);
    }







    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("C:/escape.txt")]
    [InlineData("/escape.txt")]
    public void AnEscapingEntry_IsRefusedAndCreatesNothing(string entryName)
    {
        using var dir = new TempDir();
        var evil = dir.Entry("evil.zip");

        using (var stream = File.Create(evil))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write("escaped");
        }

        var target = dir.Entry("should-not-exist");

        var error = Assert.Throws<SealExtractFailedException>(() => SealFolderPack.Extract(evil, target));

        Assert.Equal(target, error.TargetPath);
        Assert.False(Directory.Exists(target), "a refused archive must not create the target directory");
    }

    [Fact]
    public void ANestedEscape_IsAlsoRefused()
    {
        using var dir = new TempDir();
        var evil = dir.Entry("nested.zip");

        using (var stream = File.Create(evil))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            archive.CreateEntry("fine/../../escape.txt");
        }

        Assert.Throws<SealExtractFailedException>(() =>
            SealFolderPack.Extract(evil, dir.Entry("out")));
    }







    [Fact]
    public void AReparsePointInsideTheFolder_RefusesTheWholePack()
    {
        using var dir = new TempDir();

        var source = dir.Entry("with-link");
        Directory.CreateDirectory(source);
        var outside = dir.Entry("outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(System.IO.Path.Combine(outside, "secret.txt"), "x");

        var link = System.IO.Path.Combine(source, "link");
        CreateJunction(link, outside);
        Assert.True(Directory.Exists(link), "the junction was not created; the test would prove nothing");

        var zip = dir.Entry("refused.zip");

        Assert.Throws<SealPackException>(() => SealFolderPack.Pack(source, zip));
        Assert.False(File.Exists(zip), "a refused pack must leave no archive behind");
    }






    [Theory]
    [InlineData(0x00040000, true)]
    [InlineData(0x00400000, true)]
    [InlineData(0x00040000 | 0x00000080, true)]
    [InlineData(0x00000080, false)]
    [InlineData(0x00001000, false)]
    [InlineData(0x00000400, false)]
    [InlineData(0x00000020, false)]
    public void CloudPlaceholderDetectionIsExactlyTheTwoRecallBits(int attributes, bool expected)
        => Assert.Equal(expected, SealFolderPack.IsCloudPlaceholder((FileAttributes)attributes));

    [Fact]
    public void Cancellation_LeavesNoArchive()
    {
        using var dir = new TempDir();
        var source = dir.Entry("cancel-me");
        Directory.CreateDirectory(source);
        for (var i = 0; i < 20; i++)
            File.WriteAllText(System.IO.Path.Combine(source, $"f{i}.txt"), new string('x', 1024));

        var zip = dir.Entry("cancelled.zip");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => SealFolderPack.Pack(source, zip, cts.Token));
        Assert.False(File.Exists(zip));
    }

    [Fact]
    public void PackingAMissingFolder_Throws()
    {
        using var dir = new TempDir();
        Assert.Throws<DirectoryNotFoundException>(() =>
            SealFolderPack.Pack(dir.Entry("does-not-exist"), dir.Entry("x.zip")));
    }






    [Fact]
    public void ARandomFileAsArchive_IsReportedAsAnExtractionFailure()
    {
        using var dir = new TempDir();
        var broken = dir.Entry("broken.zip");
        File.WriteAllBytes(broken, Enumerable.Range(0, 512).Select(i => (byte)(i * 7)).ToArray());

        var target = dir.Entry("out");
        var error = Assert.Throws<SealExtractFailedException>(() => SealFolderPack.Extract(broken, target));

        Assert.Equal(target, error.TargetPath);
        Assert.True(File.Exists(broken), "the archive must be left in place for the caller");
    }

    [Fact]
    public void ATruncatedArchive_IsAlsoReportedAsAnExtractionFailure()
    {
        using var dir = new TempDir();

        var source = dir.Entry("src");
        Directory.CreateDirectory(source);
        File.WriteAllText(System.IO.Path.Combine(source, "a.txt"), new string('x', 4096));

        var zip = dir.Entry("whole.zip");
        SealFolderPack.Pack(source, zip);

        var truncated = dir.Entry("truncated.zip");
        var bytes = File.ReadAllBytes(zip);
        File.WriteAllBytes(truncated, bytes[..(bytes.Length / 2)]);

        var error = Assert.Throws<SealExtractFailedException>(() =>
            SealFolderPack.Extract(truncated, dir.Entry("out")));

        Assert.True(File.Exists(truncated), "the archive must be left in place for the caller");
        Assert.NotEmpty(error.TargetPath);
    }




    private static string Snapshot(string root)
    {
        var builder = new StringBuilder();

        foreach (var directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                     .Select(p => System.IO.Path.GetRelativePath(root, p))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            builder.Append("D ").Append(directory).Append('\n');
        }

        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                     .Select(p => System.IO.Path.GetRelativePath(root, p))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var full = System.IO.Path.Combine(root, file);
            builder.Append("F ").Append(file).Append(' ')
                .Append(Convert.ToHexString(File.ReadAllBytes(full))).Append('\n');
        }

        return builder.ToString();
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
