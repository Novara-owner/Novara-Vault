using System.Buffers.Binary;
using System.Diagnostics;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealKdfParamsTests
{
    private static byte[] Raw(int memoryKiB, int iterations, byte parallelism = SealKdfParams.ParallelismOnly)
    {
        var buffer = new byte[SealKdfParams.Size];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(SealKdfParams.MemoryOffset), memoryKiB);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(SealKdfParams.IterationsOffset), iterations);
        buffer[SealKdfParams.ParallelismOffset] = parallelism;
        return buffer;
    }

    private static bool TryRead(byte[] source, out SealKdfParams parameters, out SealFormatError error)
        => SealKdfParams.TryRead(source, out parameters, out error);



    [Fact]
    public void Layout_MatchesTheContract()
    {
        Assert.Equal(12, SealKdfParams.Size);
        Assert.Equal(0, SealKdfParams.MemoryOffset);
        Assert.Equal(4, SealKdfParams.IterationsOffset);
        Assert.Equal(8, SealKdfParams.ParallelismOffset);
        Assert.Equal(9, SealKdfParams.ReservedOffset);
        Assert.Equal(3, SealKdfParams.ReservedSize);
        Assert.Equal(12, SealKdfParams.ReservedOffset + SealKdfParams.ReservedSize);
        Assert.Equal(16, SealKdfParams.SaltSize);

        var bytes = new byte[SealKdfParams.Size];
        SealKdfParams.Create(65536, 3).WriteTo(bytes);
        Assert.Equal(65536, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(1, bytes[8]);
        Assert.Equal(0, bytes[9]);
        Assert.Equal(0, bytes[10]);
        Assert.Equal(0, bytes[11]);
    }

    [Fact]
    public void Default_IsSixtyFourMiBThreeIterationsOneLane()
    {
        var parameters = SealKdfParams.Default;
        Assert.Equal(65536, parameters.MemoryKiB);
        Assert.Equal(3, parameters.Iterations);
        Assert.Equal(1, parameters.Parallelism);
        Assert.True(parameters.IsInitialised);
    }

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var bytes = new byte[SealKdfParams.Size];
        SealKdfParams.Create(16384, 7).WriteTo(bytes);

        Assert.True(TryRead(bytes, out var parsed, out var error));
        Assert.Equal(SealFormatError.None, error);
        Assert.Equal(16384, parsed.MemoryKiB);
        Assert.Equal(7, parsed.Iterations);
        Assert.Equal(1, parsed.Parallelism);
    }



    [Theory]
    [InlineData(SealKdfParams.MemoryMinKiB)]
    [InlineData(SealKdfParams.MemoryMaxKiB)]
    [InlineData(65536)]
    public void MemoryInsideWindow_IsAccepted(int memoryKiB)
    {
        Assert.True(TryRead(Raw(memoryKiB, 3), out var parameters, out var error));
        Assert.Equal(SealFormatError.None, error);
        Assert.Equal(memoryKiB, parameters.MemoryKiB);
    }

    [Theory]
    [InlineData(SealKdfParams.MemoryMinKiB - 1)]
    [InlineData(SealKdfParams.MemoryMaxKiB + 1)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4 * 1024 * 1024)]
    public void MemoryOutsideWindow_IsRefused(int memoryKiB)
    {
        Assert.False(TryRead(Raw(memoryKiB, 3), out var parameters, out var error));
        Assert.Equal(SealFormatError.KdfMemory, error);
        Assert.False(parameters.IsInitialised);
    }



    [Theory]
    [InlineData(SealKdfParams.IterationsMin)]
    [InlineData(3)]
    [InlineData(SealKdfParams.IterationsMax)]
    public void IterationsInsideWindow_AreAccepted(int iterations)
    {
        Assert.True(TryRead(Raw(65536, iterations), out var parameters, out var error));
        Assert.Equal(SealFormatError.None, error);
        Assert.Equal(iterations, parameters.Iterations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SealKdfParams.IterationsMax + 1)]
    [InlineData(-1)]
    public void IterationsOutsideWindow_AreRefused(int iterations)
    {
        Assert.False(TryRead(Raw(65536, iterations), out _, out var error));
        Assert.Equal(SealFormatError.KdfIterations, error);
    }



    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)2)]
    [InlineData((byte)4)]
    [InlineData((byte)255)]
    public void LaneCountOtherThanOne_IsRefused(byte parallelism)
    {
        Assert.False(TryRead(Raw(65536, 3, parallelism), out _, out var error));
        Assert.Equal(SealFormatError.KdfParallelism, error);
    }



    [Fact]
    public void ShorterThanTwelveBytes_IsTooShort()
    {
        Assert.False(SealKdfParams.TryRead(new byte[11], out _, out var error));
        Assert.Equal(SealFormatError.TooShort, error);
    }

    [Fact]
    public void NonZeroReservedBytes_AreRefused()
    {
        for (var offset = SealKdfParams.ReservedOffset; offset < SealKdfParams.Size; offset++)
        {
            var bytes = Raw(65536, 3);
            bytes[offset] = 1;

            Assert.False(TryRead(bytes, out _, out var error));
            Assert.Equal(SealFormatError.Reserved, error);
        }
    }


    [Fact]
    public void AllZeroParameters_AreRefusedAsMemory()
    {
        Assert.False(TryRead(new byte[SealKdfParams.Size], out _, out var error));
        Assert.Equal(SealFormatError.KdfMemory, error);
    }



    [Fact]
    public void Create_RejectsOutOfRangeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SealKdfParams.Create(SealKdfParams.MemoryMinKiB - 1, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => SealKdfParams.Create(SealKdfParams.MemoryMaxKiB + 1, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => SealKdfParams.Create(65536, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SealKdfParams.Create(65536, 17));
        Assert.Throws<ArgumentOutOfRangeException>(() => SealKdfParams.Create(65536, 3, parallelism: 2));
    }

    [Fact]
    public void DefaultParameters_AreNotWritable()
    {
        var parameters = default(SealKdfParams);
        Assert.False(parameters.IsInitialised);
        Assert.Throws<InvalidOperationException>(() => parameters.WriteTo(new byte[SealKdfParams.Size]));
    }

    [Fact]
    public void WriteTo_RejectsShortDestination()
    {
        Assert.Throws<ArgumentException>(() => SealKdfParams.Default.WriteTo(new byte[11]));
    }





    [Fact]
    public void ParsingAHostileMemoryCost_AllocatesNothingAndReturnsImmediately()
    {
        var hostile = Raw(4 * 1024 * 1024, 3);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.StartNew();

        Assert.False(TryRead(hostile, out _, out var error));

        started.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(SealFormatError.KdfMemory, error);
        Assert.True(allocated < 64 * 1024, $"parse allocated {allocated} bytes");
        Assert.True(started.ElapsedMilliseconds < 250, $"parse took {started.ElapsedMilliseconds} ms");
    }




    private static SealKdfParams Cheap() => SealKdfParams.Create(SealKdfParams.MemoryMinKiB, 1);

    private static byte[] Salt(byte seed = 0)
        => Enumerable.Range(0, SealKdfParams.SaltSize).Select(i => (byte)(seed + i)).ToArray();

    [Fact]
    public void DeriveKey_IsDeterministicForTheSamePasswordSaltAndCost()
    {
        var first = Cheap().DeriveKey("correct horse battery staple", Salt());
        var second = Cheap().DeriveKey("correct horse battery staple", Salt());

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DeriveKey_ChangesWhenAnyInputChanges()
    {
        const string password = "correct horse battery staple";
        var salt = Salt();
        var baseline = Cheap().DeriveKey(password, salt);

        Assert.NotEqual(baseline, SealKdfParams.Create(SealKdfParams.MemoryMinKiB * 2, 1).DeriveKey(password, salt));
        Assert.NotEqual(baseline, SealKdfParams.Create(SealKdfParams.MemoryMinKiB, 2).DeriveKey(password, salt));
        Assert.NotEqual(baseline, Cheap().DeriveKey(password, Salt(1)));
        Assert.NotEqual(baseline, Cheap().DeriveKey(password + "x", salt));
    }

    [Fact]
    public void DeriveKey_RejectsWrongSaltLength()
    {
        Assert.Throws<ArgumentException>(() => Cheap().DeriveKey("pw", new byte[15]));
        Assert.Throws<ArgumentException>(() => Cheap().DeriveKey("pw", new byte[17]));
    }






}
