using System.Buffers.Binary;
using System.Diagnostics;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealContainerReaderTests
{
    private static byte[] Bytes(int count, byte seed)
    {
        var data = new byte[count];
        for (var i = 0; i < count; i++) data[i] = (byte)(seed + i * 37 + 11);
        return data;
    }

    private sealed record Made(byte[] Container, byte[] Plain, byte[] ContentKey, byte[] Vmk, int ChunkSize);

    private static async Task<Made> MakeAsync(byte[] plain, int chunkSize = SealFormat.DefaultChunkSize)
    {
        var vmk = Bytes(SealFormat.KeySize, 1);
        var contentKey = Bytes(SealFormat.KeySize, 2);
        var header = SealContainerWriter.PrepareHeader(
            Bytes(SealFormat.ItemIdSize, 3), (ulong)plain.Length, 1_790_000_000, chunkSize, vmk, contentKey);

        var output = new MemoryStream();
        await SealContainerWriter.WriteAsync(new MemoryStream(plain), header, contentKey, output);
        return new Made(output.ToArray(), plain, contentKey, vmk, chunkSize);
    }

    private static async Task<byte[]> ReadAllAsync(byte[] container, byte[] contentKey)
    {
        var collected = new MemoryStream();
        await SealContainerReader.ReadAsync(new MemoryStream(container), contentKey,
            async (block, token) => await collected.WriteAsync(block, token));
        return collected.ToArray();
    }

    private static async Task<SealCorruptException> RefusesAsync(byte[] container, byte[] contentKey)
        => await Assert.ThrowsAsync<SealCorruptException>(() => ReadAllAsync(container, contentKey));



    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64 * 1024)]
    [InlineData(64 * 1024 + 1)]
    [InlineData(3 * 64 * 1024)]
    public async Task RoundTrip_ReturnsTheOriginalBytes(int length)
    {
        const int chunk = 64 * 1024;
        var plain = Bytes(length, 4);
        var made = await MakeAsync(plain, chunk);

        Assert.Equal(plain, await ReadAllAsync(made.Container, made.ContentKey));
    }

    [Fact]
    public async Task RoundTrip_ReportsTheSameDigestAsTheWriter()
    {
        var plain = Bytes(12345, 5);
        var made = await MakeAsync(plain);

        var written = new MemoryStream();
        var header = SealContainerWriter.PrepareHeader(
            Bytes(SealFormat.ItemIdSize, 3), (ulong)plain.Length, 1_790_000_000, SealFormat.DefaultChunkSize,
            made.Vmk, made.ContentKey);
        var writeResult = await SealContainerWriter.WriteAsync(new MemoryStream(plain), header, made.ContentKey, written);

        var readResult = await SealContainerReader.ReadAsync(new MemoryStream(written.ToArray()), made.ContentKey,
            (_, _) => Task.CompletedTask);

        Assert.Equal(writeResult.PlaintextSha256, readResult.PlaintextSha256);
        Assert.Equal(writeResult.BlockCount, readResult.BlockCount);
        Assert.Equal((ulong)plain.Length, readResult.PlaintextLength);
    }

    [Fact]
    public async Task ZeroBytes_YieldsNoBlocksAndSucceeds()
    {
        var made = await MakeAsync(Array.Empty<byte>());

        var collected = new MemoryStream();
        var result = await SealContainerReader.ReadAsync(new MemoryStream(made.Container), made.ContentKey,
            async (block, token) => await collected.WriteAsync(block, token));

        Assert.Equal(0, result.BlockCount);
        Assert.Equal(0, collected.Length);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(Array.Empty<byte>()), result.PlaintextSha256);
    }



    [Fact]
    public async Task AlteredCiphertext_IsRejectedAsAuthentication()
    {
        var made = await MakeAsync(Bytes(4096, 6), 4096);
        var tampered = (byte[])made.Container.Clone();
        tampered[SealFormat.HeaderSize + 4 + SealFormat.NonceSize] ^= 0x01;

        var error = await RefusesAsync(tampered, made.ContentKey);
        Assert.Equal(SealCorruptReason.Authentication, error.Reason);
    }





    [Fact]
    public async Task SwappedBlocks_AreRejectedAsAuthentication()
    {
        const int chunk = 4096;
        var made = await MakeAsync(Bytes(chunk * 3, 7), chunk);

        var blockSize = 4 + SealFormat.NonceSize + chunk + SealFormat.TagSize;
        var first = SealFormat.HeaderSize;
        var second = first + blockSize;

        var tampered = (byte[])made.Container.Clone();
        var blockA = tampered.AsSpan(first, blockSize).ToArray();
        var blockB = tampered.AsSpan(second, blockSize).ToArray();
        blockB.CopyTo(tampered.AsSpan(first, blockSize));
        blockA.CopyTo(tampered.AsSpan(second, blockSize));

        var error = await RefusesAsync(tampered, made.ContentKey);
        Assert.Equal(SealCorruptReason.Authentication, error.Reason);
    }

    [Fact]
    public async Task BlockSplicedFromAnotherItem_IsRejected()
    {
        const int chunk = 4096;
        var target = await MakeAsync(Bytes(chunk * 2, 8), chunk);

        var otherVmk = Bytes(SealFormat.KeySize, 9);
        var otherKey = Bytes(SealFormat.KeySize, 10);
        var otherHeader = SealContainerWriter.PrepareHeader(
            Bytes(SealFormat.ItemIdSize, 11), (ulong)(chunk * 2), 1_790_000_000, chunk, otherVmk, otherKey);
        var otherOutput = new MemoryStream();
        await SealContainerWriter.WriteAsync(new MemoryStream(Bytes(chunk * 2, 12)), otherHeader, otherKey, otherOutput);
        var other = otherOutput.ToArray();

        var blockSize = 4 + SealFormat.NonceSize + chunk + SealFormat.TagSize;
        var tampered = (byte[])target.Container.Clone();
        other.AsSpan(SealFormat.HeaderSize, blockSize).CopyTo(tampered.AsSpan(SealFormat.HeaderSize, blockSize));


        var error = await RefusesAsync(tampered, target.ContentKey);
        Assert.Equal(SealCorruptReason.Authentication, error.Reason);
    }

    [Fact]
    public async Task TruncatedFinalBlock_IsRejected()
    {
        var made = await MakeAsync(Bytes(4096, 13), 4096);

        var error = await RefusesAsync(made.Container[..^1], made.ContentKey);
        Assert.Equal(SealCorruptReason.Truncated, error.Reason);
    }

    [Fact]
    public async Task MissingFinalBlock_IsRejected()
    {
        const int chunk = 4096;
        var made = await MakeAsync(Bytes(chunk * 2, 14), chunk);
        var blockSize = 4 + SealFormat.NonceSize + chunk + SealFormat.TagSize;

        var error = await RefusesAsync(made.Container[..(SealFormat.HeaderSize + blockSize)], made.ContentKey);
        Assert.Equal(SealCorruptReason.Truncated, error.Reason);
    }

    [Fact]
    public async Task BytesAfterTheFinalBlock_AreRejected()
    {
        var made = await MakeAsync(Bytes(8192, 15), 4096);
        var tampered = new byte[made.Container.Length + 3];
        made.Container.CopyTo(tampered, 0);

        var error = await RefusesAsync(tampered, made.ContentKey);
        Assert.Equal(SealCorruptReason.TrailingData, error.Reason);
    }






    [Fact]
    public async Task DeclaredLengthTooLarge_IsRejected()
    {
        var made = await MakeAsync(Bytes(8192, 16), 4096);
        var tampered = (byte[])made.Container.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(
            tampered.AsSpan(SealFormat.PlaintextLengthOffset, 8), 16384);

        var error = await RefusesAsync(tampered, made.ContentKey);
        Assert.Contains(error.Reason, new[] { SealCorruptReason.Authentication, SealCorruptReason.Truncated });
    }

    [Fact]
    public async Task DeclaredLengthTooSmall_IsRejected()
    {
        const int chunk = 4096;
        var made = await MakeAsync(Bytes(chunk * 2, 17), chunk);
        var tampered = (byte[])made.Container.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(
            tampered.AsSpan(SealFormat.PlaintextLengthOffset, 8), chunk / 2);

        var error = await RefusesAsync(tampered, made.ContentKey);
        Assert.Equal(SealCorruptReason.LengthMismatch, error.Reason);
    }






    [Fact]
    public async Task ReservedByteInHeader_IsRejectedAsHeaderInvalid()
    {
        var made = await MakeAsync(Bytes(8192, 18), 4096);
        var tampered = (byte[])made.Container.Clone();
        tampered[SealFormat.ReservedOffset] = 7;

        var error = await RefusesAsync(tampered, made.ContentKey);
        Assert.Equal(SealCorruptReason.HeaderInvalid, error.Reason);
    }

    [Fact]
    public async Task NewerVersion_IsReportedAsUnsupportedNotCorrupt()
    {
        var made = await MakeAsync(Bytes(100, 19));
        var tampered = (byte[])made.Container.Clone();
        tampered[SealFormat.VersionOffset] = 2;

        var error = await Assert.ThrowsAsync<SealVersionUnsupportedException>(() =>
            ReadAllAsync(tampered, made.ContentKey));
        Assert.Equal(2, error.Version);
    }

    [Fact]
    public async Task ShortHeader_IsRejectedAsTruncated()
    {
        var made = await MakeAsync(Bytes(100, 20));

        var error = await RefusesAsync(made.Container[..100], made.ContentKey);
        Assert.Equal(SealCorruptReason.Truncated, error.Reason);
    }





    [Fact]
    public async Task ZeroLengthBlock_IsRejectedAsBadLength()
    {
        var made = await MakeAsync(Bytes(4096, 21), 4096);
        var tampered = (byte[])made.Container.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            tampered.AsSpan(SealFormat.HeaderSize, 4), SealFormat.NonceSize + SealFormat.TagSize);

        var error = await RefusesAsync(tampered, made.ContentKey);
        Assert.Equal(SealCorruptReason.BlockLength, error.Reason);
    }

    [Fact]
    public async Task AbsurdBlockLength_IsRejectedBeforeAllocating()
    {
        var made = await MakeAsync(Bytes(4096, 22), 4096);
        var tampered = (byte[])made.Container.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            tampered.AsSpan(SealFormat.HeaderSize, 4), int.MaxValue);

        var started = Stopwatch.StartNew();
        var error = await RefusesAsync(tampered, made.ContentKey);
        started.Stop();

        Assert.Equal(SealCorruptReason.BlockLength, error.Reason);


        Assert.True(started.ElapsedMilliseconds < 250, $"rejecting a 2 GB block took {started.ElapsedMilliseconds} ms");
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
        var vmk = Bytes(SealFormat.KeySize, 23);
        var contentKey = Bytes(SealFormat.KeySize, 24);
        var header = SealContainerWriter.PrepareHeader(
            Bytes(SealFormat.ItemIdSize, 25), (ulong)size, 1_790_000_000, SealFormat.DefaultChunkSize, vmk, contentKey);

        var path = Path.Combine(Path.GetTempPath(), "novara-seal-reader-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024))
            {
                await SealContainerWriter.WriteAsync(new PatternStream(size), header, contentKey, output);
            }

            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024))
            {
                await SealContainerReader.ReadAsync(input, contentKey, (_, _) => Task.CompletedTask);
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
