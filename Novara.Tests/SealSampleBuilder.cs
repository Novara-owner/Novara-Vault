using System.Text;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SealSampleBuilder
{
    private const string BundlePassword = "Import-Demo-2026-north-42";
    private const string LocalPassword = "Import-Demo-2026-south-77";

    [Fact]
    public async Task BuildImportSamples()
    {
        var root = Environment.GetEnvironmentVariable("NOVARA_SEAL_SAMPLES");
        if (string.IsNullOrWhiteSpace(root)) return;

        Directory.CreateDirectory(root);


        var work = Path.Combine(Path.GetTempPath(), "novara-f21-samples-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);


        var source = Path.Combine(work, "source");
        Directory.CreateDirectory(source);

        File.WriteAllText(Path.Combine(source, "季度报告.txt"),
            "2026 年第四季度经营情况说明\n\n本文件用于目检「导入」流程。\n" + new string('-', 40) + "\n",
            new UTF8Encoding(false));

        File.WriteAllText(Path.Combine(source, "会议记录.md"),
            "# 立项评审会议记录\n\n- 时间：2026-11-05\n- 结论：通过\n",
            new UTF8Encoding(false));


        var big = new byte[1024 * 1024];
        new Random(7).NextBytes(big);
        File.WriteAllBytes(Path.Combine(source, "样本图片.JPG"), big);


        var folder = Path.Combine(source, "合同附件");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "附件一.txt"), "附件一正文\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "附件二.txt"), "附件二正文\n", new UTF8Encoding(false));


        File.WriteAllBytes(Path.Combine(source, "空文件.dat"), []);


        var spaceRoot = Path.Combine(work, "space");
        var paths = new SealSpacePaths(spaceRoot);
        paths.EnsureCreated();

        var service = new SealService(paths);
        var created = await service.InitializeAsync(LocalPassword, "目检用样例空间");
        Assert.Equal(SealServiceStatus.Ok, created);
        Assert.Equal(SealServiceStatus.Ok, await service.UnlockAsync(LocalPassword));


        var requests = new List<SealRequest>
        {
            new(Path.Combine(source, "季度报告.txt"), Note: "导入目检：普通条目"),
            new(Path.Combine(source, "会议记录.md")),
            new(Path.Combine(source, "合同附件")),
            new(Path.Combine(source, "空文件.dat")),
            new(Path.Combine(source, "样本图片.JPG"), Kind: SealedItemKind.Capsule,
                ExpirySeconds: 365L * 24 * 3600,
                Note: "导入目检：时间胶囊（一年后到期）"),
        };

        var sealedResult = await service.SealAsync(requests);
        Assert.Equal(SealServiceStatus.Ok, sealedResult.Status);
        Assert.NotNull(sealedResult.Value);
        foreach (var item in sealedResult.Value!)
        {
            Assert.True(item.Committed, $"seal failed for {item.ItemId}: {item.Error}");
            Assert.Equal(SealSealError.None, item.Error);
        }


        var kind1 = Path.Combine(root, "Novara_Space_样例_仅条目.novabundle");
        Assert.Equal(SealServiceStatus.Ok,
            await service.ExportAsync(kind1, LocalPassword, SealBundleWriter.KindItems));


        var extra = Path.Combine(work, "extra");
        Directory.CreateDirectory(extra);
        File.WriteAllText(Path.Combine(extra, "季度报告.txt"), "另一份同名文件，内容不同\n", new UTF8Encoding(false));

        var extraService = new SealService(new SealSpacePaths(Path.Combine(work, "space2")));
        Assert.Equal(SealServiceStatus.Ok, await extraService.InitializeAsync(LocalPassword));
        Assert.Equal(SealServiceStatus.Ok, await extraService.UnlockAsync(LocalPassword));
        var extraSealed = await extraService.SealAsync([new SealRequest(Path.Combine(extra, "季度报告.txt"))]);
        Assert.Equal(SealServiceStatus.Ok, extraSealed.Status);
        Assert.True(extraSealed.Value![0].Committed);

        var kind1SameName = Path.Combine(root, "Novara_Space_样例_同名不同条目.novabundle");
        Assert.Equal(SealServiceStatus.Ok,
            await extraService.ExportAsync(kind1SameName, LocalPassword, SealBundleWriter.KindItems));


        var libraryPath = Path.Combine(work, "library.novadb");
        var store = new NovaraStore(libraryPath);
        store.Load();
        store.Database.MemoEntries.Add(new MemoEntry
        {
            Name = "导入样例：资料库条目",
            Type = "自定义",
            Fields = [new EntryField { Label = "说明", Value = "资料库恢复成功后应能看到这一条", CanCopy = true }],
        });
        Assert.True(store.SaveSync());

        var backupPath = Path.Combine(work, "library.novaenc");
        Assert.True(store.ExportBackupEncrypted(backupPath, "Library-Backup-2026-west-31",
            includeFilePathEntries: false));
        var backup = await File.ReadAllBytesAsync(backupPath);

        var kind2 = Path.Combine(root, "Novara_Backup_样例_条目加资料库.novabundle");
        Assert.Equal(SealServiceStatus.Ok,
            await service.ExportAsync(kind2, LocalPassword, SealBundleWriter.KindItemsWithBackup, backup));


        var readme = string.Join("\n",
            "F21 导入流程 · 目检样例",
            "================================",
            "",
            "包内私密空间密码（两个包相同）：",
            "    " + LocalPassword,
            "",
            "kind=2 的包里那份资料库备份，密码是：",
            "    Library-Backup-2026-west-31",
            "",
            "文件说明：",
            "1. Novara_Space_样例_仅条目.novabundle",
            "   5 个条目：2 个文本、1 个约 1 MB 图片、1 个文件夹（2 个文件）、1 个 0 字节文件；",
            "   其中图片是时间胶囊（一年后到期）。用于目检 kind=1 全流程与回执「新导入 5」。",
            "",
            "2. Novara_Space_样例_同名不同条目.novabundle",
            "   1 个条目，与第 1 个包里的「季度报告.txt」同名但内容不同、id 也不同。",
            "   先导入 1 再导入 2，回执应显示「同名（不同条目）1」。",
            "",
            "3. Novara_Backup_样例_条目加资料库.novabundle",
            "   5 个条目 + 一份资料库加密备份。用于目检 kind=2 的三段两步说明；",
            "   导入后会在包所在目录生成 Novara_Backup_样例_条目加资料库.novaenc，",
            "   用上面的备份密码到「工具 → 数据备份 → 恢复备份」完成第二步。",
            "",
            "重复导入说明：同一条目重复导入会全部计入「已存在跳过」，数字对不上不是缺陷。",
            "想从头再目检一次，请删掉 %LocalAppData%\\Novara-Dev\\seal 后重新进入私密空间。",
            "");

        File.WriteAllText(Path.Combine(root, "样例说明.txt"), readme, new UTF8Encoding(false));


        try { Directory.Delete(work, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {  }
    }
}
