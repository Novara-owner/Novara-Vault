using System.Text.RegularExpressions;
using Xunit;

namespace Novara.Tests;

public class RegressionGuardsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Novara.slnx")))
            dir = dir.Parent!;
        return dir!.FullName;
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));



    [Fact]
    public void FilePicker_KeepsTheExtensionInvariant()
    {
        var src = Read("Services", "FilePicker.cs");
        Assert.Contains("SetDefaultExtension", src);
        Assert.Contains("GetExtension(path)", src);
    }



    [Fact]
    public void FilePicker_UsesNativeWin32DialogsOnly()
    {
        var code = string.Join("\n", Read("Services", "FilePicker.cs")
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//")));
        Assert.DoesNotContain("FileSavePicker", code);
        Assert.DoesNotContain("FileOpenPicker", code);
        Assert.Contains("IFileOpenDialog", code);
    }




    [Fact]
    public void FilePicker_MultiSelectUsesShellItemArray()
    {
        var src = Read("Services", "FilePicker.cs");
        Assert.Contains("GetResults(out IShellItemArray", src);
        Assert.DoesNotContain("GetResults(out IEnumShellItems", src);
        Assert.Contains("B63EA76D-1F85-456F-A19C-48159EFA858B", src);
        Assert.Contains("GetItemAt", src);
        Assert.Contains("FOS_ALLOWMULTISELECT", src);
    }




    [Fact]
    public void DiaryEditor_ImageSelectionAndMultiInsert()
    {
        var src = Read("Pages", "DiaryEditorPage.xaml.cs");
        Assert.Contains(".resizable-image-outer{{pointer-events:none}}", src);
        Assert.Contains(".resizable-image-wrapper{{pointer-events:auto}}", src);
        Assert.Contains("function clearImageSelection()", src);
        Assert.Contains("clearImageSelection();var n=", src);
        Assert.Contains("case'insertImage':insertImage(m.base64,m.filename,m.mime,m.keepSelected);break;", src);
        Assert.Contains("if(!keepSelected)clearImageSelection()", src);
        Assert.Contains("keepSelected = i == images.Count - 1", src);
    }



    [Fact]
    public void DiaryEditor_ToolbarKeepsEditorFocus()
    {
        var xaml = Read("Pages", "DiaryEditorPage.xaml");
        Assert.Contains("<Setter Property=\"AllowFocusOnInteraction\" Value=\"False\" />", xaml);
        Assert.Equal(3, Regex.Matches(xaml, "AllowFocusOnInteraction").Count);
        var cs = Read("Pages", "DiaryEditorPage.xaml.cs");
        Assert.Contains("action.StartsWith(\"exec\", StringComparison.Ordinal) || action == \"insertImage\"", cs);
        Assert.Contains("RefocusEditorAfterToolbarCommand();", cs);
    }



    [Fact]
    public void PaperTheme_ParsesSixDigitColors()
    {
        var src = Read("Themes", "PaperTheme.cs");
        Assert.Contains("if (v.Length == 6) v = \"FF\" + v;", src);
    }



    [Fact]
    public void NoHardcodedDangerRed()
    {
        foreach (var f in Directory.GetFiles(Path.Combine(RepoRoot(), "Pages"), "*.xaml", SearchOption.TopDirectoryOnly)
                     .Concat(Directory.GetFiles(Path.Combine(RepoRoot(), "Pages"), "*.xaml.cs"))
                     .Concat(new[] { Path.Combine(RepoRoot(), "MainWindow.xaml"), Path.Combine(RepoRoot(), "MainWindow.xaml.cs") }))
        {
            var src = File.ReadAllText(f);
            Assert.True(!src.Contains("FF4545", StringComparison.OrdinalIgnoreCase),
                "hardcoded danger red in " + Path.GetFileName(f));
            Assert.True(!src.Contains("CC3A3A", StringComparison.OrdinalIgnoreCase),
                "hardcoded danger-pressed red in " + Path.GetFileName(f));
        }
    }



    [Fact]
    public void CooldownHosts_KeepInlineButtonTemplates()
    {



        var hostsPerFile = new (string Dir, string File, int MinHosts)[]
        {
            ("Pages", "ToolsPage.xaml", 2),
            ("Pages", "TrashPage.xaml", 2),
            ("Pages", "LockScreenPage.xaml", 1),
        };
        var totalHosts = 0;
        foreach (var (dir, name, minHosts) in hostsPerFile)
        {
            var src = Read(dir, name);
            var matches = Regex.Matches(src, @"<services:CountdownBorder");
            Assert.True(matches.Count >= minHosts,
                "CountdownBorder hosts dropped below the pinned count in " + name + ": " + matches.Count);



            foreach (Match m in matches)
            {
                totalHosts++;
                int start = src.IndexOf("<Button", m.Index, StringComparison.Ordinal);
                Assert.True(start >= 0, "CountdownBorder without a host button: " + name);
                int end = src.IndexOf("</Button>", start, StringComparison.Ordinal);
                Assert.True(end >= 0, "unterminated host button in " + name);
                var buttonXml = src.Substring(start, end - start);


                Assert.True(buttonXml.Contains("<Button.Template>") || buttonXml.Contains("NovaraDangerButtonStyle"),
                    "CountdownBorder host without a cooldown-safe button template/style: " + name);
            }
        }
        Assert.True(totalHosts >= 5, "CountdownBorder hosts across the three pages: " + totalHosts);
    }



    [Fact]
    public void TotpRegistry_UsesStrongReferences()
    {
        var src = Read("Pages", "BasicMemoPage.xaml.cs");
        Assert.Contains("private readonly List<TotpRowRef> _totpRows = new();", src);
        Assert.DoesNotContain("ConditionalWeakTable", src);
    }



    [Fact]
    public void EditorInit_IsReentrancyGuarded()
    {
        var src = Read("Pages", "DiaryEditorPage.xaml.cs");
        Assert.Contains("private bool _editorInitInFlight;", src);
        Assert.Contains("if (_editorInitInFlight || _webViewReady) return;", src);
    }



    [Fact]
    public void EmptyGroup_VisibilityIsWorkspaceConditional()
    {
        var src = Read("Pages", "BasicMemoPage.xaml.cs");
        Assert.Contains("visibleCount > 0 || !wsActive ? Visibility.Visible : Visibility.Collapsed", src);
    }



    [Fact]
    public void EditorToolbar_ActionsSyncThePreview()
    {
        var src = Read("Pages", "DiaryEditorPage.xaml.cs");


        var lines = src.Split('\n');
        foreach (var action in new[] { "mdBold", "mdItalic", "mdStrike", "mdInlineCode", "mdHeading",
                                       "mdBullet", "mdOrdered", "mdQuote", "mdLink", "mdImage",
                                       "mdCodeBlock", "mdHr", "mdTable", "mdClearFormat", "mdUndo", "mdRedo" })
        {
            var line = lines.FirstOrDefault(l => l.Contains("function " + action + "("));
            Assert.True(line != null && line.Contains("syncAfterEdit()"),
                "toolbar action " + action + " stopped syncing the preview");
        }
    }



    [Fact]
    public void DragAutoScroll_UsesTheCompositorFrameCallback()
    {
        var src = Read("Pages", "BasicMemoPage.xaml.cs");
        Assert.Contains("CompositionTarget.Rendering += OnAutoScrollRendering", src);
    }
}
