using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Novara.Services;

public sealed class ImageExportSession
{
    private readonly Panel _host;
    private readonly WebView2 _webView;
    private string _currentHtml = "";

    private ImageExportSession(Panel host, WebView2 webView)
    {
        _host = host;
        _webView = webView;
    }


    public static async System.Threading.Tasks.Task<ImageExportSession?> BeginAsync(string html, int width, Panel host)
    {
        if (host == null) { Note("host null"); return null; }
        var webView = new WebView2
        {

            Visibility = Visibility.Collapsed,
            Width = width,
            Height = 800,
        };
        host.Children.Add(webView);
        try
        {
            var env = await WithTimeout(
                CoreWebView2Environment.CreateWithOptionsAsync(
                    null,
                    System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        CoreEnv.DataDirName, "Webview2"),
                    null).AsTask(),
                30, "env create");
            if (env == null) { Remove(webView); return null; }

            if (!await WithTimeout(webView.EnsureCoreWebView2Async(env).AsTask(), 45, "EnsureCoreWebView2")) { Remove(webView); return null; }
            var cv = webView.CoreWebView2;
            if (cv == null) { Note("CoreWebView2 null"); Remove(webView); return null; }

            var session = new ImageExportSession(host, webView);
            if (!await session.LoadAsync(html, width)) { session.Close(); return null; }
            return session;
        }
        catch (Exception ex)
        {
            Note("BeginAsync exception: " + ex);
            Remove(webView);
            return null;
        }
    }


    public async System.Threading.Tasks.Task<bool> LoadAsync(string html, int width)
    {
        _webView.Width = width;
        var cv = _webView.CoreWebView2;
        if (cv == null) { Note("LoadAsync: CoreWebView2 null"); return false; }

        var navTcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        void OnNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e) => navTcs.TrySetResult(e.IsSuccess);
        try
        {
            _webView.NavigationCompleted += OnNavCompleted;
            _webView.NavigateToString(html);
            var ok = await WithTimeout(navTcs.Task, 30, "navigation");
            if (ok != true) return false;
            await System.Threading.Tasks.Task.Delay(400);
            _currentHtml = html;
            return true;
        }
        catch (Exception ex)
        {
            Note("LoadAsync exception: " + ex);
            return false;
        }
        finally
        {
            _webView.NavigationCompleted -= OnNavCompleted;
        }
    }




    public async System.Threading.Tasks.Task<(int Parts, bool Completed)> ScreenshotToFileAsync(string filePath)
    {
        var cv = _webView.CoreWebView2;
        if (cv == null || string.IsNullOrEmpty(filePath)) { Note("Screenshot: no core or path"); return (0, false); }
        int slice = 0;
        try
        {
            var width = (int)Math.Round(_webView.Width);
            var shJson = await RunScriptAsync(cv, "document.body.scrollHeight");
            if (shJson == null) return (0, false);
            int total;
            using (var doc = System.Text.Json.JsonDocument.Parse(shJson))
                total = (int)Math.Ceiling(doc.RootElement.GetDouble());
            if (total <= 0) { Note("scrollHeight <= 0: " + shJson); return (0, false); }

            ImageExportService.CleanStaleGroupFiles(filePath);

            var dir = System.IO.Path.GetDirectoryName(filePath) ?? "";
            var stem = System.IO.Path.GetFileNameWithoutExtension(filePath);
            var ext = System.IO.Path.GetExtension(filePath);
            var pdfPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"novara-imgexp-{Guid.NewGuid():N}.pdf");

            try
            {
                for (int y = 0; y < total; y += ImageExportTemplates.MaxSliceHeight)
                {
                    var h = Math.Min(ImageExportTemplates.MaxSliceHeight, total - y);


                    if (!await LoadAsync(InjectSliceCss(_currentHtml, y, h), width)) return (slice, false);

                    var ps = cv.Environment.CreatePrintSettings();
                    ps.PageWidth = width / 96.0;
                    ps.PageHeight = h / 96.0;
                    ps.MarginTop = ps.MarginBottom = ps.MarginLeft = ps.MarginRight = 0;
                    ps.ShouldPrintBackgrounds = true;
                    using var printCts = new System.Threading.CancellationTokenSource();
                    var printed = await WithTimeout<bool>(cv.PrintToPdfAsync(pdfPath, ps).AsTask(printCts.Token), 30, $"PrintToPdf slice {slice}");
                    if (printed != true)
                    {
                        try { printCts.Cancel(); } catch { }
                        Note($"slice {slice}: PrintToPdf returned false");
                        return (slice, false);
                    }

                    var bytes = await RenderPdfPageToPngAsync(pdfPath, width);
                    if (bytes == null) return (slice, false);
                    var outPath = slice == 0 ? filePath : System.IO.Path.Combine(dir, stem + "_" + (slice + 1) + ext);
                    System.IO.File.WriteAllBytes(outPath, bytes);
                    slice++;
                }
            }
            finally
            {
                try { if (System.IO.File.Exists(pdfPath)) System.IO.File.Delete(pdfPath); } catch { }
            }
            return (slice, true);
        }
        catch (Exception ex)
        {
            Note("Screenshot exception: " + ex);
            return (slice, false);
        }
    }




    public void Close()
    {
        _host.Children.Remove(_webView);
        try { _webView.Close(); } catch { }
    }




    private static string InjectSliceCss(string html, int y, int h)
    {
        var headClose = html.LastIndexOf("</head>", StringComparison.Ordinal);
        if (headClose < 0) { Note("InjectSliceCss: </head> not found"); return html; }
        return html.Insert(headClose,
            $"<style>html{{overflow:hidden;height:{h}px}}body{{position:relative;top:-{y}px}}</style>");
    }


    private static async System.Threading.Tasks.Task<byte[]?> RenderPdfPageToPngAsync(string pdfPath, int cssWidth)
    {
        try
        {
            var pdfStorage = await WithTimeout(Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath).AsTask(), 15, "pdf StorageFile");
            if (pdfStorage == null) return null;
            var pdfDoc = await WithTimeout(Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(pdfStorage).AsTask(), 20, "PdfDocument load");
            if (pdfDoc == null) return null;
            if (pdfDoc.PageCount < 1) { Note("PdfDocument: zero pages"); return null; }
            var page = pdfDoc.GetPage(0);

            var mem = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var opts = new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = (uint)(cssWidth * 2) };
            if (!await WithTimeout(page.RenderToStreamAsync(mem, opts).AsTask(), 45, "PdfPage render")) return null;

            var size = (uint)mem.Size;
            using var dr = new Windows.Storage.Streams.DataReader(mem.GetInputStreamAt(0));
            var loaded = await WithTimeout(dr.LoadAsync(size).AsTask(), 15, "read render buffer");
            if (loaded != size) { Note($"render buffer truncated: {loaded}/{size}"); return null; }
            var bytes = new byte[size];
            dr.ReadBytes(bytes);
            if (bytes.Length == 0) { Note("render: zero bytes"); return null; }
            return bytes;
        }
        catch (Exception ex)
        {
            Note("RenderPdfPage exception: " + ex);
            return null;
        }
    }

    private static void Remove(WebView2 webView)
    {
        var parent = webView.Parent as Panel;
        parent?.Children.Remove(webView);
        try { webView.Close(); } catch { }
    }



    private static async System.Threading.Tasks.Task<T?> WithTimeout<T>(System.Threading.Tasks.Task<T> task, int seconds, string step)
    {
        var done = await System.Threading.Tasks.Task.WhenAny(task, System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(seconds)));
        if (done != task)
        {
            Note($"{step} TIMEOUT {seconds}s");
            return default;
        }
        return task.Result;
    }


    private static async System.Threading.Tasks.Task<bool> WithTimeout(System.Threading.Tasks.Task task, int seconds, string step)
    {
        var done = await System.Threading.Tasks.Task.WhenAny(task, System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(seconds)));
        if (done != task)
        {
            Note($"{step} TIMEOUT {seconds}s");
            return false;
        }
        return true;
    }

    private static async System.Threading.Tasks.Task<string?> RunScriptAsync(CoreWebView2 cv, string script)
    {
        try
        {
            return await WithTimeout(cv.ExecuteScriptAsync(script).AsTask(), 15, "ExecuteScript " + script);
        }
        catch (Exception ex) { Note("ExecuteScript exception: " + ex.Message); return null; }
    }

    private static void Note(string message)
        => CrashLogger.LogNote("ImageExport", message.Length > 1500 ? message[..1500] : message);
}

public static class ImageExportService
{


    public static string? TryReadLogoSvg()
    {
        try
        {
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.svg");
            return System.IO.File.Exists(logoPath) ? System.IO.File.ReadAllText(logoPath) : null;
        }
        catch { return null; }
    }


    public static string SiblingPath(string path, string suffix)
    {
        var dir = System.IO.Path.GetDirectoryName(path) ?? "";
        var name = System.IO.Path.GetFileNameWithoutExtension(path) + suffix + System.IO.Path.GetExtension(path);
        return System.IO.Path.Combine(dir, name);
    }



    public static void DeleteIfExist(params string?[] paths)
    {
        foreach (var p in paths)
        {
            try { if (!string.IsNullOrEmpty(p) && System.IO.File.Exists(p)) System.IO.File.Delete(p); }
            catch (Exception ex) { CrashLogger.LogNote("ImageExport", "delete failed: " + ex.Message); }
        }
    }



    public static void DeleteGroupFiles(string basePath, int parts)
    {
        if (string.IsNullOrEmpty(basePath)) return;
        var dir = System.IO.Path.GetDirectoryName(basePath) ?? "";
        var stem = System.IO.Path.GetFileNameWithoutExtension(basePath);
        var ext = System.IO.Path.GetExtension(basePath);
        DeleteIfExist(basePath);
        for (int i = 2; i <= parts; i++)
            DeleteIfExist(System.IO.Path.Combine(dir, stem + "_" + i + ext));
    }




    public static void CleanStaleGroupFiles(string basePath)
    {
        try
        {
            if (string.IsNullOrEmpty(basePath)) return;
            var dir = System.IO.Path.GetDirectoryName(basePath) ?? "";
            var stem = System.IO.Path.GetFileNameWithoutExtension(basePath);
            var ext = System.IO.Path.GetExtension(basePath);
            foreach (var f in System.IO.Directory.EnumerateFiles(string.IsNullOrEmpty(dir) ? "." : dir, stem + "*" + ext))
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(f);
                var tail = name.Length > stem.Length ? name[stem.Length..] : "";
                if (tail == "_mobile"
                    || System.Text.RegularExpressions.Regex.IsMatch(tail, @"^_mobile_\d{1,2}$")
                    || System.Text.RegularExpressions.Regex.IsMatch(tail, @"^_\d{1,2}$"))
                {
                    try { System.IO.File.Delete(f); } catch (Exception ex) { CrashLogger.LogNote("ImageExport", "stale clean failed: " + ex.Message); }
                }
            }
        }
        catch (Exception ex) { CrashLogger.LogNote("ImageExport", "stale clean failed: " + ex.Message); }
    }



    public static void SweepStaleTempPdfs()
    {
        try
        {
            foreach (var f in System.IO.Directory.EnumerateFiles(System.IO.Path.GetTempPath(), "novara-imgexp-*.pdf"))
            {
                try { System.IO.File.Delete(f); } catch { }
            }
        }
        catch { }
    }
}
