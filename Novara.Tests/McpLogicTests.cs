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
    public void ReadMemo_RedactsEmailPassword_Cvv_AndTotp()
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
    public void ReadMemo_RedactsCardNumber_AndIdNumber()
    {
        var db = NewDb();
        var id = McpLogic.CreateMemo(db, "我的卡", "银行卡", "6222000011112222", new List<McpFieldInput>
        {
            new() { Label = "卡号", Value = "6222 0000 1111 2222", CanCopy = true },
            new() { Label = "持卡人", Value = "张三", CanCopy = false }
        }, null, null);

        var view = McpLogic.ReadItem(db, "memo", id);

        var card = view.Fields!.First(f => f.Label == "卡号");
        Assert.Equal("****", card.Value);
        Assert.True(card.Redacted);
        Assert.Equal("张三", view.Fields!.First(f => f.Label == "持卡人").Value);
        Assert.Equal("****", view.KeyInfo);
    }

    [Fact]
    public void Search_DoesNotProbeACardNumber()
    {
        var db = NewDb();
        McpLogic.CreateMemo(db, "我的卡", "银行卡", "6222000011112222", new List<McpFieldInput>
        {
            new() { Label = "备注", Value = "主卡", CanCopy = false }
        }, null, null);
        McpLogic.CreateMemo(db, "身份证", "证件", "备注值", new List<McpFieldInput>
        {
            new() { Label = "证件号", Value = "110101199001011234", CanCopy = true }
        }, null, null);

        Assert.NotEmpty(McpLogic.SearchItems(db, "主卡", null));
        Assert.Empty(McpLogic.SearchItems(db, "6222000011112222", null));
        Assert.Empty(McpLogic.SearchItems(db, "6222", null));
        Assert.Empty(McpLogic.SearchItems(db, "110101199001011234", null));
    }

    [Fact]
    public void UpdateMemo_CannotUnmaskKeyInfoByChangingType()
    {
        var db = NewDb();
        var id = McpLogic.CreateMemo(db, "我的卡", "银行卡", "6222000011112222", new List<McpFieldInput>
        {
            new() { Label = "备注", Value = "主卡", CanCopy = false }
        }, null, null);




        var ex = Assert.Throws<McpError>(() => McpLogic.UpdateMemo(db, id, null, "自定义", null, null, null, null));
        Assert.Contains("绕过脱敏", ex.Message);
        Assert.Equal("****", McpLogic.ReadItem(db, "memo", id).KeyInfo);



        McpLogic.UpdateMemo(db, id, null, "自定义", "", null, null, null);
        Assert.Equal("自定义", McpLogic.ReadItem(db, "memo", id).MemoType);
        Assert.Equal("", McpLogic.ReadItem(db, "memo", id).KeyInfo);


        var id2 = McpLogic.CreateMemo(db, "证件", "证件", "110101199001011234", null, null, null);
        McpLogic.UpdateMemo(db, id2, null, "银行卡", null, null, null, null);
        Assert.Equal("****", McpLogic.ReadItem(db, "memo", id2).KeyInfo);
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


        var db = NewDb();
        var id = McpLogic.CreateDiary(db, "文档", "内容", "markdown");
        var before = DateTime.Now.AddHours(-1);
        db.DiaryItems.First(d => d.Id == id).ModifiedAt = before;

        McpLogic.UpdateDiary(db, id, "文档", "内容");
        Assert.Equal(before, db.DiaryItems.First(d => d.Id == id).ModifiedAt);

        Assert.Throws<McpError>(() => McpLogic.UpdateDiary(db, id, "文档", ""));
    }

    [Fact]
    public void ListItems_InvalidType_ReturnsEmpty()
    {
        var db = NewDb();
        Assert.Empty(McpLogic.ListItems(db, "bogus"));
    }






    [Fact]
    public void UpdateTodo_RejectsBlankMainText_WithoutRenamingTheCard()
    {
        var db = NewDb();
        var id = McpLogic.CreateTodo(db, "原标题", "原正文", null, null);

        Assert.Throws<McpError>(() => McpLogic.UpdateTodo(db, id, "新标题", "   ", null, null));

        var card = db.TodoCards.Single(x => x.Id.ToString() == id);
        Assert.Equal("原标题", card.Title);
        Assert.Equal("原正文", card.MainText);
    }

    [Fact]
    public void UpdateNote_RejectsBlankContent_WithoutRenamingTheCard()
    {
        var db = NewDb();
        var id = McpLogic.CreateNote(db, "原标题", "原正文", null);

        Assert.Throws<McpError>(() => McpLogic.UpdateNote(db, id, "新标题", "   ", null));

        var card = db.NoteCards.Single(x => x.Id.ToString() == id);
        Assert.Equal("原标题", card.Title);
        Assert.Equal("原正文", card.Content);
    }

    [Fact]
    public void UpdateDiary_RejectsEmptyContent_WithoutRenamingOrRestamping()
    {
        var db = NewDb();
        var id = McpLogic.CreateDiary(db, "原标题", "原正文", "markdown");
        var stamp = DateTime.Now.AddHours(-1);
        db.DiaryItems.First(d => d.Id == id).ModifiedAt = stamp;

        Assert.Throws<McpError>(() => McpLogic.UpdateDiary(db, id, "新标题", ""));
        Assert.Throws<McpError>(() => McpLogic.UpdateDiary(db, id, null, " \t "));

        var doc = db.DiaryItems.First(d => d.Id == id);
        Assert.Equal("原标题", doc.Title);
        Assert.Equal("原正文", doc.Content);
        Assert.Equal(stamp, doc.ModifiedAt);
    }

    [Fact]
    public void UpdateMemo_RejectsMissingGroup_WithoutApplyingTheOtherFields()
    {
        var db = NewDb();
        var id = McpLogic.CreateMemo(db, "原标题", "账户", "alice", null, null, null);


        Assert.Throws<McpError>(() => McpLogic.UpdateMemo(db, id, "新标题", "网站", "new-keyinfo",
            null, Guid.NewGuid(), "new-icon"));

        var e = db.MemoEntries.Single(x => x.Id.ToString() == id);
        Assert.Equal("原标题", e.Name);
        Assert.Equal("账户", e.Type);
        Assert.Equal("alice", e.KeyInfo);
        Assert.NotEqual("new-icon", e.IconKey);
    }

    [Fact]
    public void UpdatePath_RejectsBlankPath_WithoutRenaming()
    {
        var db = NewDb();
        var id = McpLogic.CreatePath(db, "原名", @"C:\a.txt", null);

        Assert.Throws<McpError>(() => McpLogic.UpdatePath(db, id, "新名", "   ", null));

        var e = db.PathBackupItems.Single(x => x.Id.ToString() == id);
        Assert.Equal("原名", e.Name);
        Assert.Equal(@"C:\a.txt", e.Path);
    }
}
