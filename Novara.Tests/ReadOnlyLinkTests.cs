using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class ReadOnlyLinkTests
{
    private const string SpaceId = "lzdnSHE-yodGlgpMR8HcfA";

    [Fact]
    public void HttpsServer_ProducesTheWebReaderLink()
    {
        var link = ReadOnlyLink.Build("https://novara.example.com", SpaceId);
        Assert.NotNull(link);
        Assert.Equal($"https://novara.example.com/web/#s={SpaceId}", link!.Url);
        Assert.True(link.Secure);
    }

    [Fact]
    public void ExplicitPortIsKept()
    {
        var link = ReadOnlyLink.Build("https://nas.local:8443", SpaceId);
        Assert.Equal($"https://nas.local:8443/web/#s={SpaceId}", link!.Url);
    }


    [Fact]
    public void TrailingSlashOnTheServerUrl_DoesNotDoubleTheSeparator()
    {
        var link = ReadOnlyLink.Build("https://novara.example.com/", SpaceId);
        Assert.Equal($"https://novara.example.com/web/#s={SpaceId}", link!.Url);
    }


    [Fact]
    public void PathInTheServerUrl_IsDropped()
    {
        var link = ReadOnlyLink.Build("https://novara.example.com/some/prefix", SpaceId);
        Assert.Equal($"https://novara.example.com/web/#s={SpaceId}", link!.Url);
    }

    [Fact]
    public void PlainHttpOnALanAddress_IsReportedAsNotSecure()
    {
        var link = ReadOnlyLink.Build("http://192.168.1.5:5190", SpaceId);
        Assert.False(link!.Secure);
    }

    [Theory]
    [InlineData("http://localhost:5190")]
    [InlineData("http://127.0.0.1:5190")]
    [InlineData("http://[::1]:5190")]
    public void PlainHttpOnLoopback_CountsAsSecure(string serverUrl)
    {

        Assert.True(ReadOnlyLink.Build(serverUrl, SpaceId)!.Secure);
    }

    [Theory]
    [InlineData(null, SpaceId)]
    [InlineData("", SpaceId)]
    [InlineData("https://novara.example.com", null)]
    [InlineData("https://novara.example.com", "")]
    [InlineData("not-a-url", SpaceId)]
    public void IncompleteInput_ProducesNoLink(string? serverUrl, string? spaceId)
    {
        Assert.Null(ReadOnlyLink.Build(serverUrl, spaceId));
    }


    [Fact]
    public void LinkCarriesNoCredentialMaterial()
    {
        var link = ReadOnlyLink.Build("https://novara.example.com", SpaceId)!;
        Assert.DoesNotContain("token", link.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", link.Url, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("#s=" + SpaceId, link.Url, StringComparison.Ordinal);
    }
}
