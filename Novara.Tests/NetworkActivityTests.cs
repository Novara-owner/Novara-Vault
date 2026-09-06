using Novara.Services;
using Xunit;

public class NetworkActivityTests
{
    [Fact]
    public void BeginEnd_BasicLifecycle()
    {
        NetworkActivityService.Begin("NetActivity_Kind_Probe", "https://api.example.com/v1");
        Assert.True(NetworkActivityService.IsActive);
        Assert.Equal(("NetActivity_Kind_Probe", "api.example.com"), NetworkActivityService.Current);

        NetworkActivityService.End();
        Assert.False(NetworkActivityService.IsActive);
        Assert.Equal("api.example.com", NetworkActivityService.Recent[0].Host);
        Assert.True(NetworkActivityService.Recent[0].Success);
    }

    [Fact]
    public void Begin_NestedSessions_PopInReverseOrder()
    {
        NetworkActivityService.Begin("NetActivity_Kind_Diagnose", "https://outer.example.com");
        NetworkActivityService.Begin("NetActivity_Kind_Relay", "https://inner.example.com"); // N5-S15-03: Chat kind removed (dead key, no production caller)
        Assert.Equal("inner.example.com", NetworkActivityService.Current!.Value.Host);

        NetworkActivityService.End();
        Assert.Equal("outer.example.com", NetworkActivityService.Current!.Value.Host);
        NetworkActivityService.End();
        Assert.False(NetworkActivityService.IsActive);
    }

    [Fact]
    public void End_EmptyStack_IsSafe()
    {
        NetworkActivityService.End(); // must not throw
        Assert.False(NetworkActivityService.IsActive);
    }

    [Fact]
    public void Recent_RollsOverAt50()
    {
        for (int i = 0; i < 55; i++)
        {
            NetworkActivityService.Begin("NetActivity_Kind_Probe", $"https://h{i}.example.com");
            NetworkActivityService.End();
        }
        Assert.True(NetworkActivityService.Recent.Count <= 50);
        Assert.Equal("h54.example.com", NetworkActivityService.Recent[0].Host); // newest first
    }

    [Fact]
    public void ExtractHost_HandlesSchemelessAndEmpty()
    {
        Assert.Equal("a.example.com", NetworkActivityService.ExtractHost("https://a.example.com/x"));
        Assert.Equal("b.example.com", NetworkActivityService.ExtractHost("b.example.com"));
        Assert.Equal("", NetworkActivityService.ExtractHost(""));
    }

    [Fact]
    public void ClearRecent_EmptiesTrail()
    {
        NetworkActivityService.Begin("NetActivity_Kind_Probe", "https://x.example.com");
        NetworkActivityService.End();
        NetworkActivityService.ClearRecent();
        Assert.Empty(NetworkActivityService.Recent);
    }
}
