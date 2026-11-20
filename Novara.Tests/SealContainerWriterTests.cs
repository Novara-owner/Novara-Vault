using System.Buffers.Binary;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealContainerWriterTests
{
    private const long SealedAt = 1_790_000_000;

    private static byte[] Bytes(int count, byte seed)
    {
        var data = new byte[count];
        for (var i = 0; i < count; i++) data[i] = (byte)(seed + i * 31 + 7);
        return data;
    }

    private static byte[] ItemId() => Bytes(SealFormat.ItemIdSize, 1);

    private sealed record Sealed(byte[] Container, SealWriteResult Result, SealContainerHeader Header, byte[] ContentKey, byte[] Vmk);

    private static async Task<Sealed> SealAsync(byte[] plain, int chunkSize = SealFormat.DefaultChunkSize)
    {
        var vmk = Bytes(SealFormat.KeySize, 2);
        var contentKey = Bytes(SealFormat.KeySize, 3);
        var header = SealContainerWriter.PrepareHeader(
            ItemId(), (ulong)plain.Length, SealedAt, chunkSize, vmk, contentKey);

        var output = new MemoryStream();
        var result = await SealContainerWriter.WriteAsync(new MemoryStream(plain), header, contentKey, output);

        return new Sealed(output.ToArray(), result, header, contentKey, vmk);
    }



    [Fact]
    public async Task ZeroBytes_ProducesAHeaderAndNoBlocks()
    {
        var sealedResult = await SealAsync(Array.Empty<byte>());

        Assert.Equal(0, sealedResult.Result.BlockCount);
        Assert.Equal(SealFormat.HeaderSize, sealedResult.Container.Length);
        Assert.True(SealContainerHeader.TryRead(sealedResult.Container, out var parsed, out _));
        Assert.Equal(0UL, parsed.PlaintextLength);
        Assert.Equal(SHA256Of(Array.Empty<byte>()), sealedResult.Result.PlaintextSha256);
    }

    [Fact]
    public async Task OneByte_ProducesASingleFinalBlock()
    {
        var sealedResult = await SealAsync([0x42]);

        Assert.Equal(1, sealedResult.Result.BlockCount);
        Assert.Equal(SealFormat.HeaderSize + 4 + SealFormat.NonceSize + 1 + SealFormat.TagSize,
            sealedResult.Container.Length);
    }





    [Fact]
    public async Task ExactlyOneChunk_ProducesOneFullFinalBlock()
    {
        const int chunk = 64 * 1024;
        var sealedResult = await SealAsync(Bytes(chunk, 4), chunk);

        Assert.Equal(1, sealedResult.Result.BlockCount);
        Assert.Equal(SealFormat.HeaderSize + 4 + SealFormat.NonceSize + chunk + SealFormat.TagSize,
            sealedResult.Container.Length);
        Assert.True(ReadBlock(sealedResult.Container, SealFormat.HeaderSize, out var length, out _, out _));
        Assert.Equal(SealFormat.NonceSize + chunk + SealFormat.TagSize, length);
    }

    [Fact]
    public async Task OneChunkPlusOne_ProducesTwoBlocks()
    {
        const int chunk = 64 * 1024;
        var sealedResult = await SealAsync(Bytes(chunk + 1, 5), chunk);

        Assert.Equal(2, sealedResult.Result.BlockCount);
        Assert.Equal(
            SealFormat.HeaderSize
            + (4 + SealFormat.NonceSize + chunk + SealFormat.TagSize)
            + (4 + SealFormat.NonceSize + 1 + SealFormat.TagSize),
            sealedResult.Container.Length);
    }

    [Fact]
    public async Task ThreeChunks_ProduceThreeBlocks()
    {
        const int chunk = 32 * 1024;
        var sealedResult = await SealAsync(Bytes(chunk * 3, 6), chunk);

        Assert.Equal(3, sealedResult.Result.BlockCount);
    }







    [Fact]
    public async Task EveryBlockDecryptsWithItsOwnAad()
    {
        const int chunk = 32 * 1024;
        var plain = Bytes(chunk * 2 + 17, 7);
        var sealedResult = await SealAsync(plain, chunk);

        var offset = SealFormat.HeaderSize;
        var recovered = new List<byte>();

        for (var index = 0; index < sealedResult.Result.BlockCount; index++)
        {
            Assert.True(ReadBlock(sealedResult.Container, offset, out var payloadLength, out var nonce, out var payload));

            var isFinal = index == sealedResult.Result.BlockCount - 1;
            var aad = SealContainerWriter.BuildBlockAad(ItemId(), index, isFinal);
            recovered.AddRange(CryptoService.DecryptGcmWithNonce(payload, sealedResult.ContentKey, nonce, aad));

            offset += 4 + payloadLength;
        }

        Assert.Equal(offset, sealedResult.Container.Length);
        Assert.Equal(plain, recovered.ToArray());
    }

    [Fact]
    public async Task LengthPrefixCoversNonceCipherAndTag()
    {
        var sealedResult = await SealAsync(Bytes(100, 8));

        Assert.True(ReadBlock(sealedResult.Container, SealFormat.HeaderSize, out var payloadLength, out _, out var payload));
        Assert.Equal(SealFormat.NonceSize + 100 + SealFormat.TagSize, payloadLength);

        Assert.Equal(payloadLength - SealFormat.NonceSize, payload.Length);
    }

    [Fact]
    public async Task HeaderRoundTripsWithTheDeclaredLength()
    {
        var sealedResult = await SealAsync(Bytes(1234, 9));

        Assert.True(SealContainerHeader.TryRead(sealedResult.Container, out var parsed, out var error));
        Assert.Equal(SealFormatError.None, error);
        Assert.Equal(1234UL, parsed.PlaintextLength);
        Assert.Equal(SealedAt, parsed.SealedAtUtc);
        Assert.Equal(SealFormat.DefaultChunkSize, parsed.ChunkSize);
        Assert.Equal(ItemId(), parsed.ItemId.ToArray());
    }

    [Fact]
    public async Task PlaintextDigest_IsOfTheSourceNotTheContainer()
    {
        var plain = Bytes(4096, 10);
        var sealedResult = await SealAsync(plain);

        Assert.Equal(SHA256Of(plain), sealedResult.Result.PlaintextSha256);
    }



    [Fact]
    public void PrepareHeader_ProducesAKeySlotThatOpensWithTheVmk()
    {
        var vmk = Bytes(SealFormat.KeySize, 11);
        var contentKey = Bytes(SealFormat.KeySize, 12);

        var header = SealContainerWriter.PrepareHeader(ItemId(), 4096, SealedAt, 64 * 1024, vmk, contentKey);

        var full = new byte[SealFormat.HeaderSize];
        header.WriteTo(full);
        var prefix = full[..SealFormat.AuthenticatedPrefixSize];

        Assert.True(SealKeySlot.TryUnwrap(vmk, header.KeySlot, prefix, out var opened, out var error));
        Assert.Equal(SealFormatError.None, error);
        Assert.Equal(contentKey, opened);
    }

    [Fact]
    public void PrepareHeader_RejectsWrongSizes()
    {
        var vmk = Bytes(SealFormat.KeySize, 13);
        var contentKey = Bytes(SealFormat.KeySize, 14);

        Assert.Throws<ArgumentException>(() =>
            SealContainerWriter.PrepareHeader(new byte[15], 0, SealedAt, 64 * 1024, vmk, contentKey));
        Assert.Throws<ArgumentException>(() =>
            SealContainerWriter.PrepareHeader(ItemId(), 0, SealedAt, 64 * 1024, new byte[31], contentKey));
        Assert.Throws<ArgumentException>(() =>
            SealContainerWriter.PrepareHeader(ItemId(), 0, SealedAt, 64 * 1024, vmk, new byte[31]));
    }



    [Fact]
    public async Task SourceShorterThanDeclared_Throws()
    {
        var vmk = Bytes(SealFormat.KeySize, 15);
        var contentKey = Bytes(SealFormat.KeySize, 16);
        var header = SealContainerWriter.PrepareHeader(ItemId(), 100, SealedAt, 64 * 1024, vmk, contentKey);

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            SealContainerWriter.WriteAsync(new MemoryStream(Bytes(50, 17)), header, contentKey, new MemoryStream()));
    }

    [Fact]
    public async Task SourceLongerThanDeclared_Throws()
    {
        var vmk = Bytes(SealFormat.KeySize, 18);
        var contentKey = Bytes(SealFormat.KeySize, 19);
        var header = SealContainerWriter.PrepareHeader(ItemId(), 50, SealedAt, 64 * 1024, vmk, contentKey);



        await Assert.ThrowsAsync<IOException>(() =>
            SealContainerWriter.WriteAsync(new MemoryStream(Bytes(100, 20)), header, contentKey, new MemoryStream()));
    }



    [Fact]
    public async Task Cancellation_StopsTheWrite()
    {
        var vmk = Bytes(SealFormat.KeySize, 21);
        var contentKey = Bytes(SealFormat.KeySize, 22);
        const int chunk = 32 * 1024;
        var header = SealContainerWriter.PrepareHeader(ItemId(), chunk * 8, SealedAt, chunk, vmk, contentKey);

        using var cts = new CancellationTokenSource();
        var progress = new InlineProgress(value =>
        {
            if (value >= chunk * 2) cts.Cancel();
        });

        var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SealContainerWriter.WriteAsync(new PatternStream(chunk * 8), header, contentKey, output, progress, cts.Token));


        Assert.True(output.Length < SealFormat.HeaderSize + chunk * 8L);
    }







    [Fact]
    public async Task Memory_DoesNotGrowWithFileSize()
    {
        var small = await MeasureSurvivingMemoryAsync(3L * 1024 * 1024);
        var large = await MeasureSurvivingMemoryAsync(200L * 1024 * 1024);

        Assert.True(large - small < 8L * 1024 * 1024,
            $"surviving memory grew by {(large - small) / 1024 / 1024} MiB from 3 MiB to 200 MiB");
    }

    private static async Task<long> MeasureSurvivingMemoryAsync(long size)
    {
        var contentKey = Bytes(SealFormat.KeySize, 23);
        var vmk = Bytes(SealFormat.KeySize, 24);
        var header = SealContainerWriter.PrepareHeader(
            ItemId(), (ulong)size, SealedAt, SealFormat.DefaultChunkSize, vmk, contentKey);

        var path = Path.Combine(Path.GetTempPath(), "novara-seal-writer-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024))
            {
                await SealContainerWriter.WriteAsync(new PatternStream(size), header, contentKey, output);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetTotalMemory(forceFullCollection: true);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }



    private static byte[] SHA256Of(byte[] data) => System.Security.Cryptography.SHA256.HashData(data);

    private static bool ReadBlock(byte[] container, int offset, out int payloadLength, out byte[] nonce, out byte[] payload)
    {
        nonce = Array.Empty<byte>();
        payload = Array.Empty<byte>();
        payloadLength = 0;

        if (offset + 4 > container.Length) return false;

        payloadLength = BinaryPrimitives.ReadInt32LittleEndian(container.AsSpan(offset, 4));
        if (offset + 4 + payloadLength > container.Length) return false;

        nonce = container.AsSpan(offset + 4, SealFormat.NonceSize).ToArray();
        payload = container.AsSpan(offset + 4 + SealFormat.NonceSize, payloadLength - SealFormat.NonceSize).ToArray();
        return true;
    }


    private sealed class InlineProgress(Action<long> action) : IProgress<long>
    {
        public void Report(long value) => action(value);
    }


    private sealed class PatternStream(long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= length) return 0;

            var take = (int)Math.Min(count, length - _position);
            for (var i = 0; i < take; i++) buffer[offset + i] = (byte)((_position + i) * 31 + 7);
            _position += take;
            return take;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
