namespace Novara.Services;






public static class UpdateHttp
{
    public static async Task<byte[]> ReadCappedAsync(HttpContent? content, int maxBytes, CancellationToken ct)
    {
        if (content == null) throw new InvalidOperationException("update response has no body");
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > maxBytes)
                throw new InvalidOperationException($"update response exceeds the {maxBytes} byte safety limit");
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }
}
