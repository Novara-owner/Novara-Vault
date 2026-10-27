using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;



public class MemoDisplayModeTests
{
    [Theory]
    [InlineData(null, MemoDisplayMode.Auto)]
    [InlineData("", MemoDisplayMode.Auto)]
    [InlineData("   ", MemoDisplayMode.Auto)]
    [InlineData("auto", MemoDisplayMode.Auto)]
    [InlineData("fixed", MemoDisplayMode.Fixed)]
    [InlineData("FIXED", MemoDisplayMode.Fixed)]
    [InlineData("Fixed", MemoDisplayMode.Fixed)]
    [InlineData("memory", MemoDisplayMode.Memory)]
    [InlineData("MEMORY", MemoDisplayMode.Memory)]
    [InlineData("Memory", MemoDisplayMode.Memory)]
    [InlineData("expand", MemoDisplayMode.Auto)]
    [InlineData("固定展开", MemoDisplayMode.Auto)]
    public void Normalize_FallsBackToAutoForUnknownValues(string? raw, string expected)
        => Assert.Equal(expected, MemoDisplayMode.Normalize(raw));

    [Fact]
    public void Constants_AreThePersistedContract()
    {
        Assert.Equal("auto", MemoDisplayMode.Auto);
        Assert.Equal("fixed", MemoDisplayMode.Fixed);
        Assert.Equal("memory", MemoDisplayMode.Memory);
    }



    [Theory]
    [InlineData(nameof(TodoCard), "isExpanded")]
    [InlineData(nameof(MemoGroup), "isExpanded")]
    [InlineData(nameof(AppSettings), "ungroupedCardExpanded")]
    public void UntouchedModels_SerializeWithoutMemoryFields(string typeName, string jsonKey)
    {
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        string json = typeName switch
        {
            nameof(TodoCard) => System.Text.Json.JsonSerializer.Serialize(new TodoCard(), options),
            nameof(MemoGroup) => System.Text.Json.JsonSerializer.Serialize(new MemoGroup(), options),
            _ => System.Text.Json.JsonSerializer.Serialize(new AppSettings(), options),
        };
        Assert.DoesNotContain($"\"{jsonKey}\"", json);


        string withValue = typeName switch
        {
            nameof(TodoCard) => System.Text.Json.JsonSerializer.Serialize(new TodoCard { IsExpanded = false }, options),
            nameof(MemoGroup) => System.Text.Json.JsonSerializer.Serialize(new MemoGroup { IsExpanded = true }, options),
            _ => System.Text.Json.JsonSerializer.Serialize(new AppSettings { UngroupedCardExpanded = true }, options),
        };
        Assert.Contains($"\"{jsonKey}\"", withValue);
    }
}
