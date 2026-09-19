namespace Novara.Services;


public sealed record ReadOnlyLink(string Url, bool Secure)
{












    public static ReadOnlyLink? Build(string? serverUrl, string? spaceId)
    {
        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(spaceId)) return null;
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri)) return null;

        var url = $"{uri.Scheme}://{uri.Authority}/web/#s={spaceId}";



        var secure = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || uri.IsLoopback;
        return new ReadOnlyLink(url, secure);
    }
}
