using Novara.Services;
using Xunit;

namespace Novara.Tests;


public class PlanCollapsePolicyTests
{
    [Fact]
    public void NotePreview_IsFiveLines()
        => Assert.Equal(5, PlanCollapsePolicy.NotePreviewLines);

    [Fact]
    public void TodoPreview_IsTwoRealRows()
        => Assert.Equal(2, PlanCollapsePolicy.TodoPreviewRows);

    [Fact]
    public void TodoPreviewRow_Is32Pixels()
        => Assert.Equal(32, PlanCollapsePolicy.TodoPreviewRowHeight);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(10, true)]
    public void TodoExpandButton_OnlyWhenMoreThanTwoSubItems(int subCount, bool expected)
        => Assert.Equal(expected, PlanCollapsePolicy.ShouldShowTodoExpand(subCount));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoteExpandButton_MirrorsRealLayoutTrim(bool trimmed)
        => Assert.Equal(trimmed, PlanCollapsePolicy.ShouldShowNoteExpand(trimmed));
}
