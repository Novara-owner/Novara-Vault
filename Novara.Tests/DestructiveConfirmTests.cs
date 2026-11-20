using Novara.Services;
using Xunit;

namespace Novara.Tests;






public class DestructiveConfirmTests
{
    private const string Zh = "我确认删除私密空间，且明白无法恢复";

    [Theory]
    [InlineData("我确认删除私密空间，且明白无法恢复")]
    [InlineData("我确认删除私密空间且明白无法恢复")]
    [InlineData("我确认删除私密空间, 且明白无法恢复。")]
    [InlineData("  我确认删除私密空间，且明白无法恢复  ")]
    [InlineData("我确认删除私密空间、且明白无法恢复")]
    public void ChinesePhrase_IgnoresPunctuationAndWhitespace(string typed)
        => Assert.True(DestructiveConfirm.Matches(typed, Zh));

    [Theory]
    [InlineData("我确认删除私密空间")]
    [InlineData("我确认删除全部数据，且明白无法恢复")]
    [InlineData("")]
    [InlineData("   ")]
    public void WrongOrEmptyPhrase_IsRefused(string typed)
        => Assert.False(DestructiveConfirm.Matches(typed, Zh));

    [Fact]
    public void FullWidthAndHalfWidthForms_AreEquivalent()
    {

        Assert.True(DestructiveConfirm.Matches("Ｉ　ｃｏｎｆｉｒｍ", "I confirm"));
    }

    [Fact]
    public void EnglishPhrase_IsCaseInsensitive()
    {
        const string en = "I confirm deleting the private space and understand it cannot be undone";
        Assert.True(DestructiveConfirm.Matches(
            "i CONFIRM deleting the private space, and understand it cannot be undone.", en));
        Assert.False(DestructiveConfirm.Matches("I confirm erasing the main vault", en));
    }

    [Fact]
    public void EveryShippedPhrase_MatchesItself()
    {

        string[] phrases =
        {
            "我确认删除私密空间，且明白无法恢复",
            "I confirm deleting the private space and understand it cannot be undone",
            "我確認刪除私密空間，且明白無法復原",
            "프라이빗 스페이스 삭제를 확인하며 복구할 수 없음을 이해합니다",
            "プライベートスペースを削除し復元できないことを理解しています",
        };
        foreach (var phrase in phrases)
            Assert.True(DestructiveConfirm.Matches(phrase, phrase), phrase);
    }

    [Fact]
    public void DifferentPhrases_DoNotCollide()
    {

        Assert.False(DestructiveConfirm.Matches(
            "我确认清空主库，且明白无法恢复", "我确认删除私密空间，且明白无法恢复"));
    }
}
