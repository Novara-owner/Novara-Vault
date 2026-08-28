using Novara.Services;
using Xunit;

namespace Novara.Tests;


[Collection("CoreSequential")]
public class McpAuditLogTests : IDisposable
{
    private readonly string _dir;

    public McpAuditLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "novara-audit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        McpAuditLog.SetBaseDir(_dir);
    }

    public void Dispose()
    {
        try { foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); } catch { }
        try { Directory.Delete(_dir, recursive: true); } catch { } 
        McpAuditLog.SetBaseDir(null);
    }

    private static McpAuditEvent Evt(string ev = "call", string tool = "read_item", bool ok = true,
        string reason = "", string title = "标题", string path = @"C:\Agents\node.exe")
        => new()
        {
            Ev = ev, Path = path, Tool = tool, Target = "diary:abcd1234",
            Title = title, Write = false, Ok = ok, Reason = reason
        };

    [Fact]
    public void Write_ThenReadLatest_RoundTripsFields()
    {
        McpAuditLog.Write(Evt());
        var list = McpAuditLog.ReadLatest(10);

        Assert.Single(list);
        var back = list[0];
        Assert.Equal("call", back.Ev);
        Assert.Equal(@"C:\Agents\node.exe", back.Path);
        Assert.Equal("node.exe", back.Client);      
        Assert.Equal("read_item", back.Tool);
        Assert.Equal("diary:abcd1234", back.Target);
        Assert.Equal("标题", back.Title);
        Assert.True(back.Ok);
        Assert.False(back.Write);
        Assert.True(back.Ts.Length > 0);
    }

    [Fact]
    public void ReadLatest_IsNewestFirst_AndRespectsMax()
    {
        for (var i = 0; i < 5; i++) McpAuditLog.Write(Evt(title: $"t{i}"));

        var top2 = McpAuditLog.ReadLatest(2);
        Assert.Equal(2, top2.Count);
        Assert.Equal("t4", top2[0].Title);
        Assert.Equal("t3", top2[1].Title);
    }

    [Fact]
    public void ClearAll_RemovesEverything()
    {
        McpAuditLog.Write(Evt());
        Assert.NotEmpty(McpAuditLog.ReadLatest(10));

        McpAuditLog.ClearAll();
        Assert.Empty(McpAuditLog.ReadLatest(10));
    }

    [Fact]
    public void SecretShapedDetails_AreMasked()
    {
        McpAuditLog.Write(new McpAuditEvent
        {
            Ev = "call", Path = @"C:\x\a.exe", Tool = "search_items", Ok = false,
            Reason = "key=sk-abcdefghijklmnop1234 and Authorization Bearer abcdefgh12345678"
        });

        var back = McpAuditLog.ReadLatest(1)[0];
        Assert.DoesNotContain("sk-abcdefghijklmnop", back.Reason);
        Assert.Contains("sk-***", back.Reason);
        Assert.DoesNotContain("abcdefgh12345678", back.Reason);
    }

    [Fact]
    public void TruncateTitle_CapsAt60Chars()
    {
        var longTitle = new string('字', 100);
        var cut = McpAuditLog.TruncateTitle(longTitle);

        Assert.True(cut.Length <= 61); 
        Assert.EndsWith("…", cut);
        Assert.StartsWith(new string('字', 60), cut);
        Assert.Equal("", McpAuditLog.TruncateTitle("   "));
    }

    [Fact]
    public async System.Threading.Tasks.Task ConcurrentWrites_ProducePerLineValidJson()
    {
        const int n = 32;
        await System.Threading.Tasks.Task.WhenAll(
            Enumerable.Range(0, n).Select(_ => System.Threading.Tasks.Task.Run(() => McpAuditLog.Write(Evt()))));

        
        var all = McpAuditLog.ReadLatest(n * 2);
        Assert.Equal(n, all.Count);
    }
}
