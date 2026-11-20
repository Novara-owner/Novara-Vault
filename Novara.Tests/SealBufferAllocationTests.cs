using Novara.Services;
using Xunit;

namespace Novara.Tests;






public class SealBufferAllocationTests
{
    private static readonly byte[] Vmk =
        Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0xA0 + i)).ToArray();

    private static readonly byte[] ContentKey =
        Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0xC0 + i)).ToArray();

    private const long SealedAt = 1_790_000_000;
    private const int PlainBytes = 8 * 1024 * 1024;

    [Fact]
    public async Task WritingAndReadingReuseTheirBlockBuffers()
    {
        var itemId = Enumerable.Range(0, SealFormat.ItemIdSize).Select(i => (byte)i).ToArray();
        var plain = new byte[PlainBytes];
        for (var i = 0; i < plain.Length; i++) plain[i] = (byte)(i * 31 + 7);

        var header = SealContainerWriter.PrepareHeader(
            itemId, (ulong)plain.Length, SealedAt, SealFormat.DefaultChunkSize, Vmk, ContentKey);

        var destination = new MemoryStream(PlainBytes + 65536);
        var source = new MemoryStream(plain);

        var writeBefore = GC.GetAllocatedBytesForCurrentThread();
        await SealContainerWriter.WriteAsync(source, header, ContentKey, destination, null, CancellationToken.None);
        var writeAllocated = GC.GetAllocatedBytesForCurrentThread() - writeBefore;


        Assert.True(writeAllocated < 8 * 1024 * 1024,
            $"writer allocated {writeAllocated} bytes for an 8 MB file");

        var container = destination.ToArray();
        var input = new MemoryStream(container);

        var readBefore = GC.GetAllocatedBytesForCurrentThread();
        var read = await SealContainerReader.ReadAsync(
            input, ContentKey, static (_, _) => Task.CompletedTask, null, CancellationToken.None);
        var readAllocated = GC.GetAllocatedBytesForCurrentThread() - readBefore;


        Assert.True(readAllocated < 8 * 1024 * 1024,
            $"reader allocated {readAllocated} bytes for an 8 MB file");
        Assert.Equal(PlainBytes, (int)read.PlaintextLength);
    }
}
