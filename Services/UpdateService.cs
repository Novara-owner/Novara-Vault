using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Novara.Services;





public static class UpdateService
{
    public const string ManifestUrl = "https://novara.xin/downloads/latest.json";


    public sealed record LatestRelease(string Version, string? Date, string Url, string Sha256, string? NotesZh, string? NotesEn);
    public sealed record CheckResult(LatestRelease? Latest, bool HasNewer, string? Error);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly HttpClient DownloadHttp = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static string CurrentVersion =>
        (typeof(UpdateService).Assembly.GetName().Version)?.ToString(3) ?? "0.0.0";



    public static bool IsNewer(string latestVersion, string currentVersion)
        => UpdatePolicy.IsNewer(latestVersion, currentVersion);

    public static async Task<CheckResult> CheckLatestAsync(CancellationToken ct)
    {
        NetworkActivityService.Begin("NetActivity_Kind_Update", ManifestUrl);
        var ok = false;
        try
        {
            using var resp = await Http.GetAsync(ManifestUrl, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            var version = root.GetProperty("version").GetString() ?? "";
            var url = root.GetProperty("url").GetString() ?? "";
            var sha = root.GetProperty("sha256").GetString() ?? "";
            string? date = root.TryGetProperty("date", out var d) ? d.GetString() : null;
            string? zh = null, en = null;
            if (root.TryGetProperty("notes", out var notes))
            {
                zh = notes.TryGetProperty("zh-CN", out var a) ? a.GetString() : null;
                en = notes.TryGetProperty("en-US", out var b) ? b.GetString() : null;
            }
            var rel = new LatestRelease(version.Trim(), date, url.Trim(), sha.Trim(), zh, en);
            ok = true;
            return new CheckResult(rel, IsNewer(rel.Version, CurrentVersion), null);
        }
        catch (OperationCanceledException)
        {
            return new CheckResult(null, false, "cancelled");
        }
        catch (Exception ex)
        {
            return new CheckResult(null, false, ex.Message);
        }
        finally
        {
            NetworkActivityService.End(ok);
        }
    }




    public static async Task<string> DownloadAsync(LatestRelease rel, IProgress<double> progress, CancellationToken ct)
    {
        NetworkActivityService.Begin("NetActivity_Kind_Update", rel.Url);
        var ok = false;
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                App.DataDirName, "updates");
            Directory.CreateDirectory(dir);
            foreach (var stale in Directory.EnumerateFiles(dir))
            {
                try { File.Delete(stale); } catch { }
            }



            if (!Uri.TryCreate(rel.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new InvalidOperationException("update manifest url must be an absolute https url: " + rel.Url);
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "Novara_Setup.exe";
            var dest = Path.Combine(dir, fileName);
            try
            {
                using var resp = await DownloadHttp.GetAsync(rel.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? -1;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var file = File.Create(dest);
                var buffer = new byte[81920];
                long readTotal = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                    readTotal += n;
                    if (total > 0) progress.Report(Math.Round(readTotal * 100.0 / total, 1));
                }
                progress.Report(100);
                ok = true;
                return dest;
            }
            catch
            {
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                throw;
            }
        }
        finally
        {
            NetworkActivityService.End(ok);
        }
    }

    public static string Sha256Hex(string filePath)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }



    public static void LaunchInstaller(string path)
    {
        Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /NORESTART")
        { UseShellExecute = true });
    }
}
