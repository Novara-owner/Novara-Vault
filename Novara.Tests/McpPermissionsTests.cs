using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;


[Collection("CoreSequential")]
public class McpPermissionsTests
{
    

    [Theory]
    [InlineData("create_memo", McpPerm.MemoCreate)]
    [InlineData("create_path", McpPerm.PathCreate)]
    [InlineData("create_todo", McpPerm.TodoCreate)]
    [InlineData("create_note", McpPerm.NoteCreate)]
    [InlineData("create_diary", McpPerm.DiaryCreate)]
    [InlineData("update_memo", McpPerm.MemoUpdate)]
    [InlineData("update_diary", McpPerm.DiaryUpdate)]
    [InlineData("update_path", McpPerm.PathUpdate)]
    public void RequiredFor_WriteTools_MapToZoneBit(string method, McpPerm expected)
        => Assert.Equal(expected, McpPermissions.RequiredFor(method, null));

    [Theory]
    [InlineData("delete_item", "memo", McpPerm.MemoDelete)]
    [InlineData("delete_item", "todo", McpPerm.TodoDelete)]
    [InlineData("delete_item", "diary", McpPerm.DiaryDelete)]
    [InlineData("read_item", "note", McpPerm.NoteRead)]
    [InlineData("read_item", "path", McpPerm.PathRead)]
    [InlineData("list_items", "memo", McpPerm.MemoRead)]
    [InlineData("search_items", "todo", McpPerm.TodoRead)]
    public void RequiredFor_TypedTools_MapToZoneBit(string method, string type, McpPerm expected)
        => Assert.Equal(expected, McpPermissions.RequiredFor(method, type));

    [Fact]
    public void RequiredFor_AllType_RequiresEveryRead()
    {
        // N2-67: an explicit "all" is NOT a valid type (execution layer rejects it) - the permission
        
        Assert.Equal(McpPerm.None, McpPermissions.RequiredFor("list_items", "all"));
        Assert.Equal(McpPermissions.AllRead, McpPermissions.RequiredFor("list_items", null)); 
        Assert.Equal(McpPerm.None, McpPermissions.RequiredFor("search_items", "all"));
    }

    [Fact]
    public void RequiredFor_UnknownMethod_Or_InvalidType_MapsToNone()
    {
        Assert.Equal(McpPerm.None, McpPermissions.RequiredFor("hack_db", "memo")); 
        Assert.Equal(McpPerm.None, McpPermissions.RequiredFor("create_bogus", null));
        Assert.Equal(McpPerm.None, McpPermissions.RequiredFor("read_item", "bogus"));
        Assert.Equal(McpPerm.None, McpPermissions.RequiredFor("delete_item", "bogus"));
    }

    

    [Fact]
    public void DefaultSet_ReadsEverythingExceptMemo_CannotWrite()
    {
        
        Assert.Equal(McpPerm.PathRead | McpPerm.TodoRead | McpPerm.NoteRead | McpPerm.DiaryRead,
            McpPermissions.DefaultForNewClient);
        Assert.Equal(McpPerm.None, McpPermissions.DefaultForNewClient & McpPerm.MemoRead);
        Assert.Equal(McpPerm.None, McpPermissions.DefaultForNewClient & (McpPerm.MemoCreate | McpPerm.TodoCreate | McpPerm.DiaryDelete | McpPerm.NoteUpdate));
    }

    [Fact]
    public void LegacySet_IsFullTwentyBits()
    {
        
        Assert.Equal(McpPermissions.LegacyFull, McpPermissions.DefaultForNewClient | ~McpPermissions.DefaultForNewClient & McpPermissions.LegacyFull);
        Assert.True(McpPermissions.LegacyFull.HasFlag(McpPerm.MemoRead));
        Assert.True(McpPermissions.LegacyFull.HasFlag(McpPerm.DiaryDelete));
        Assert.Equal(20, System.Numerics.BitOperations.PopCount((ulong)(long)McpPermissions.LegacyFull));
    }

    

    private static AppSettings SettingsWithLegacy(List<string> paths)
        => new() { McpAllowedProcesses = paths };

    [Fact]
    public void Migrate_LegacyPaths_GetFullSet_Once()
    {
        var s = SettingsWithLegacy(new List<string> { @"C:\a.exe", @"C:\b.exe" });
        Assert.True(McpPermissions.EnsureMigrated(s));
        Assert.Equal(2, s.McpClientPermissions.Count);
        Assert.All(s.McpClientPermissions, r => Assert.Equal((long)McpPermissions.LegacyFull, r.Permissions));
        Assert.False(McpPermissions.EnsureMigrated(s)); 
        Assert.Equal(2, s.McpClientPermissions.Count);
    }

    [Fact]
    public void Migrate_EmptyLists_NoGhostRevival()
    {
        var s = SettingsWithLegacy(new List<string>());
        Assert.True(McpPermissions.EnsureMigrated(s)); 
        Assert.Empty(s.McpClientPermissions);
        
        Assert.False(McpPermissions.EnsureMigrated(s));
        Assert.Empty(s.McpClientPermissions);
    }

    [Fact]
    public void EnsureDefaultRecord_Idempotent_And_GetFor()
    {
        var s = SettingsWithLegacy(new List<string>());
        McpPermissions.EnsureDefaultRecord(s, @"C:\agent.exe");
        McpPermissions.EnsureDefaultRecord(s, @"C:\agent.exe"); 
        Assert.Single(s.McpClientPermissions);
        Assert.Equal(McpPermissions.DefaultForNewClient, McpPermissions.GetFor(s, @"C:\agent.exe"));
        Assert.Equal(McpPerm.None, McpPermissions.GetFor(s, @"C:\other.exe"));
    }

    

    [Fact]
    public void Describe_AllRead_And_SingleBit()
    {
        Assert.Equal("all:read", McpPermissions.Describe(McpPermissions.AllRead));
        Assert.Equal("memo:read", McpPermissions.Describe(McpPerm.MemoRead));
        Assert.Equal("none", McpPermissions.Describe(McpPerm.None));
    }
}
