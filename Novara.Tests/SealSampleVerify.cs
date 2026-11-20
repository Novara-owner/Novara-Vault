using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SealSampleVerify
{
    private const string BundlePassword = "Import-Demo-2026-south-77";
    private const string BackupPassword = "Library-Backup-2026-west-31";

    [Fact]
    public async Task TheSamplePackagesHoldWhatTheReadmeClaims()
    {
        var root = Environment.GetEnvironmentVariable("NOVARA_SEAL_SAMPLES");
        if (string.IsNullOrWhiteSpace(root)) return;

        var onlyItems = Path.Combine(root, "Novara_Space_样例_仅条目.novabundle");
        var sameName = Path.Combine(root, "Novara_Space_样例_同名不同条目.novabundle");
        var withBackup = Path.Combine(root, "Novara_Backup_样例_条目加资料库.novabundle");


        var peek1 = SealBundleReader.Peek(onlyItems);
        Assert.Equal(SealBundleReadStatus.Ok, peek1.Status);
        Assert.Equal(SealBundleWriter.KindItems, peek1.Peek!.Kind);
        Assert.Equal(5, peek1.Peek.ItemCount);

        var open1 = await SealBundleReader.OpenAsync(onlyItems, BundlePassword);
        Assert.Equal(SealBundleReadStatus.Ok, open1.Status);
        Assert.Equal(5, open1.Bundle!.Index.Items.Count);
        Assert.Single(open1.Bundle.Index.Items, i => i.IsCapsule);
        Assert.Contains(open1.Bundle.Index.Items, i => i.DisplayName == "合同附件");
        Assert.Contains(open1.Bundle.Index.Items, i => i.DisplayName == "空文件.dat");
        Assert.Equal(0u, open1.Bundle.Index.Items.Single(i => i.DisplayName == "空文件.dat").Size);


        Assert.Equal(SealBundleReadStatus.WrongPassword,
            (await SealBundleReader.OpenAsync(onlyItems, "not-the-password")).Status);


        var open2 = await SealBundleReader.OpenAsync(sameName, BundlePassword);
        Assert.Equal(SealBundleReadStatus.Ok, open2.Status);
        Assert.Single(open2.Bundle!.Index.Items);

        var a = open1.Bundle.Index.Items.Single(i => i.DisplayName == "季度报告.txt");
        var b = open2.Bundle.Index.Items.Single();
        Assert.Equal("季度报告.txt", b.DisplayName);
        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(a.ContentSha256, b.ContentSha256);


        var peek3 = SealBundleReader.Peek(withBackup);
        Assert.Equal(SealBundleReadStatus.Ok, peek3.Status);
        Assert.True(peek3.Peek!.HasBackup);
        Assert.Equal(5, peek3.Peek.ItemCount);

        var extracted = Path.Combine(Path.GetTempPath(), "novara-f21-verify-" + Guid.NewGuid().ToString("N") + ".novaenc");
        try
        {
            Assert.Equal(SealBundleReadStatus.Ok,
                await SealBundleReader.ExtractBackupAsync(withBackup, BundlePassword, extracted));

            var target = new NovaraStore(Path.Combine(Path.GetTempPath(),
                "novara-f21-lib-" + Guid.NewGuid().ToString("N") + ".novadb"));
            var loaded = target.ImportBackup(extracted, BackupPassword);
            Assert.Equal(LoadStatus.Ok, loaded.Status);
            Assert.Contains(target.Database.MemoEntries, m => m.Name == "导入样例：资料库条目");
        }
        finally
        {
            try { if (File.Exists(extracted)) File.Delete(extracted); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {  }
        }
    }
}
