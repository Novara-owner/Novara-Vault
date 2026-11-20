using Novara.Services;
using System.Text.RegularExpressions;
using Xunit;

namespace Novara.Tests;

public class SealGlyphTests
{
    [Fact]
    public void TheSetIsCompleteAndWellFormed()
    {
        Assert.Equal(84, SealGlyphs.All.Count);
        Assert.Equal(84, SealGlyphs.All.Select(g => g.Label).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(493, SealGlyphs.All.Sum(g => g.Layers.Length));
        Assert.Equal(SealGlyphs.FolderLabel, SealGlyphs.All[0].Label);

        foreach (var glyph in SealGlyphs.All)
        {
            Assert.NotEmpty(glyph.Layers);
            foreach (var layer in glyph.Layers)
            {
                Assert.False(string.IsNullOrWhiteSpace(layer.Data));
                Assert.Equal(6, layer.Fill.Length);
                Assert.Matches("^[0-9A-Fa-f]{6}$", layer.Fill);




                Assert.False(
                    Regex.IsMatch(layer.Data, "[a-z]"),
                    $"glyph {glyph.Label} has a lowercase path command: {layer.Data[..Math.Min(60, layer.Data.Length)]}");
            }
        }

        Assert.NotNull(SealGlyphs.Find(SealGlyphs.FolderLabel));
        Assert.NotNull(SealGlyphs.Find(SealGlyphs.UnknownLabel));
    }

    [Theory]
    [InlineData("report.pdf", false, "PDF")]
    [InlineData("REPORT.PDF", false, "PDF")]
    [InlineData("photo.jpeg", false, "JPG")]
    [InlineData("photo.tif", false, "TIFF")]
    [InlineData("page.htm", false, "HTML")]
    [InlineData("config.yml", false, "YAML")]
    [InlineData("notes.docx", false, "DOCX")]
    [InlineData("archive.7z", false, "7Z")]
    [InlineData("no-extension-at-all", false, "未知")]
    [InlineData("mystery.qqq", false, "未知")]
    [InlineData("dotted.name.with.dots.txt", false, "TXT")]
    [InlineData("a.bin", true, "文件夹")]
    public void DetectionIsByExtensionOnly(string name, bool isFolder, string expected)
        => Assert.Equal(expected, SealGlyphs.Detect(name, isFolder).Label);

    [Fact]
    public void AnEmptyOrNullNameFallsBackToUnknown()
    {
        Assert.Equal(SealGlyphs.UnknownLabel, SealGlyphs.Detect(null, false).Label);
        Assert.Equal(SealGlyphs.UnknownLabel, SealGlyphs.Detect(string.Empty, false).Label);
        Assert.Equal(SealGlyphs.UnknownLabel, SealGlyphs.Detect("trailingdot.", false).Label);
    }
}
