using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;



public class McpLogicTests
{
    private static NovaraDatabase NewDb() => new()
    {
        MemoGroups = new List<MemoGroup> { new() { Id = Guid.NewGuid(), Name = "工作" } }
    };

    [Fact]
    public void ReadMemo_RedactsSensitiveFields_KeepsKeyInfoAndNonSensitive()
    {
        var db = NewDb();
        var id = McpLogic.CreateMemo(db, "GitHub", "账户", "alice", new List<McpFieldInput>
        {
            new() { Label = "密码", Value = "secret123", CanCopy = true },
            new() { Label = "备注", Value = "工作账号", CanCopy = false }
        }, null, null);

        var view = McpLogic.ReadItem(db, "memo", id);

        Assert.Equal("alice", view.KeyInfo);            
        var pw = view.Fields!.First(f => f.Label == "密码");
        Assert.Equal("****", pw.Value);
        Assert.True(pw.Redacted);
        var note = view.Fields!.First(f => f.Label == "备注");
        Assert.Equal("工作账号", note.Value);
        Assert.False(note.Redacted);
    }

    [Fact]
    public void ReadMemo_RedactsEmailPassword_Cvv_AndTotp() // N3-01: the three UI labels that were missing from SensitiveLabels
    {
        var db = NewDb();
        var id = McpLogic.CreateMemo(db, "钱包", "银行卡", "4111", new List<McpFieldInput>
        {
            new() { Label = "邮箱密码", Value = "mail-secret", CanCopy = true },
            new() { Label = "CVV", Value = "123", CanCopy = true },
            new() { Label = "TOTP", Value = "JBSWY3DPEHPK3PXP", CanCopy = false }
        }, null, null);

        var view = McpLogic.ReadItem(db, "memo", id);

        Assert.All(view.Fields!, f => { Assert.Equal("****", f.Value); Assert.True(f.Redacted); });
    }

    [Fact]
    public void UpdateMemo_CanAddSensitiveField_ButCannotModifyExisting()
    {
        var db = NewDb();
        var id = McpLogic.CreateMemo(db, "GitHub", "账户", "alice", new List<McpFieldInput>
        {
            new() { Label = "密码", Value = "old-pass", CanCopy = true }
        }, null, null);

        
        McpLogic.UpdateMemo(db, id, null, null, null, new List<McpFieldInput>
        {
            new() { Label = "密码", Value = "old-pass", CanCopy = true },
            new() { Label = "API Key", Value = "sk-123", CanCopy = true }
        }, null, null);
        Assert.Equal(2, McpLogic.ReadItem(db, "memo", id).Fields!.Count);

        
        Assert.Throws<McpError>(() => McpLogic.UpdateMemo(db, id, null, null, null, new List<McpFieldInput>
        {
            new() { Label = "密码", Value = "hacked", CanCopy = true }
        }, null, null));
    }

    [Fact]
    public void CreateListDelete_SoftDeleteAndListExcludesDeleted()
    {
        var db = NewDb();
        var id = McpLogic.CreateNote(db, "便签", "内容", null);
        Assert.Single(McpLogic.ListItems(db, "note"));

        McpLogic.DeleteItem(db, "note", id);
        Assert.Empty(McpLogic.ListItems(db, "note"));
        Assert.True(db.NoteCards.Single().IsDeleted);
    }

    [Fact]
    public void Search_HitsContent_ButNeverLeaksSensitiveValue()
    {
        var db = NewDb();
        McpLogic.CreateNote(db, "项目笔记", "关于 Novara 的 MCP 方案", null);
        McpLogic.CreateMemo(db, "GitHub", "账户", "alice", new List<McpFieldInput>
        {
            new() { Label = "密码", Value = "topsecret-mcp", CanCopy = true }
        }, null, null);

        Assert.NotEmpty(McpLogic.SearchItems(db, "MCP", null));       
        Assert.Empty(McpLogic.SearchItems(db, "topsecret-mcp", null)); 
    }

    [Fact]
    public void CreateDiary_RejectsInvalidFormat()
    {
        var db = NewDb();
        Assert.Throws<McpError>(() => McpLogic.CreateDiary(db, "t", "c", "pdf"));
    }

    [Fact]
    public void CreateMemo_RejectsMissingGroup()
    {
        var db = NewDb();
        Assert.Throws<McpError>(() => McpLogic.CreateMemo(db, "x", "账户", null, null, Guid.NewGuid(), null));
    }

    [Fact]
    public void CreateDiary_DefaultsToMarkdown_WhenFormatEmpty()
    {
        var db = NewDb();
        var id = McpLogic.CreateDiary(db, "文档", "内容", null);
        Assert.Equal("markdown", McpLogic.ReadItem(db, "diary", id).Format);
    }

    [Fact]
    public void UpdateDiary_NoopDoesNotBumpModifiedAt_AndRejectsEmptyContent()
    {
        // N3-26: a no-op update (id only / identical values) must not push the entry to the top of
        // "recently modified"; empty content is rejected like UpdateTodo/UpdateNote.
        var db = NewDb();
        var id = McpLogic.CreateDiary(db, "文档", "内容", "markdown");
        var before = DateTime.Now.AddHours(-1);
        db.DiaryItems.First(d => d.Id == id).ModifiedAt = before;

        McpLogic.UpdateDiary(db, id, "文档", "内容"); // identical values -> no change
        Assert.Equal(before, db.DiaryItems.First(d => d.Id == id).ModifiedAt);

        Assert.Throws<McpError>(() => McpLogic.UpdateDiary(db, id, "文档", ""));
    }

    [Fact]
    public void ListItems_InvalidType_ReturnsEmpty()
    {
        var db = NewDb();
        Assert.Empty(McpLogic.ListItems(db, "bogus"));
    }
}
