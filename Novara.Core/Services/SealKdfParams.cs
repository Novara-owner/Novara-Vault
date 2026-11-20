using System.Buffers.Binary;

namespace Novara.Services;









public readonly struct SealKdfParams
{
    public const int Size = 12;
    public const int SaltSize = 16;

    public const int MemoryOffset = 0;
    public const int IterationsOffset = 4;
    public const int ParallelismOffset = 8;
    public const int ReservedOffset = 9;
    public const int ReservedSize = 3;


    public const int MemoryMinKiB = 8 * 1024;


    public const int MemoryMaxKiB = 1024 * 1024;

    public const int MemoryDefaultKiB = 64 * 1024;
    public const int IterationsMin = 1;
    public const int IterationsMax = 16;
    public const int IterationsDefault = 3;
    public const int ParallelismOnly = 1;

    public int MemoryKiB { get; }
    public int Iterations { get; }
    public int Parallelism { get; }


    public bool IsInitialised => MemoryKiB != 0;

    private SealKdfParams(int memoryKiB, int iterations, int parallelism)
    {
        MemoryKiB = memoryKiB;
        Iterations = iterations;
        Parallelism = parallelism;
    }


    public static SealKdfParams Default => Create(MemoryDefaultKiB, IterationsDefault);

    public static SealKdfParams Create(int memoryKiB, int iterations, int parallelism = ParallelismOnly)
    {
        if (memoryKiB < MemoryMinKiB || memoryKiB > MemoryMaxKiB)
            throw new ArgumentOutOfRangeException(nameof(memoryKiB),
                $"memory must be within [{MemoryMinKiB}, {MemoryMaxKiB}] KiB");
        if (iterations < IterationsMin || iterations > IterationsMax)
            throw new ArgumentOutOfRangeException(nameof(iterations),
                $"iterations must be within [{IterationsMin}, {IterationsMax}]");
        if (parallelism != ParallelismOnly)
            throw new ArgumentOutOfRangeException(nameof(parallelism),
                "the contract pins the lane count to 1");

        return new SealKdfParams(memoryKiB, iterations, parallelism);
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"destination must hold {Size} bytes", nameof(destination));
        if (!IsInitialised)
            throw new InvalidOperationException("kdf parameters were never initialised");

        var span = destination[..Size];
        span.Clear();

        BinaryPrimitives.WriteInt32LittleEndian(span[MemoryOffset..], MemoryKiB);
        BinaryPrimitives.WriteInt32LittleEndian(span[IterationsOffset..], Iterations);
        span[ParallelismOffset] = (byte)Parallelism;
    }





    public static bool TryRead(ReadOnlySpan<byte> source, out SealKdfParams parameters, out SealFormatError error)
    {
        parameters = default;

        if (source.Length < Size)
        {
            error = SealFormatError.TooShort;
            return false;
        }

        var span = source[..Size];

        for (var i = 0; i < ReservedSize; i++)
        {
            if (span[ReservedOffset + i] != 0)
            {
                error = SealFormatError.Reserved;
                return false;
            }
        }

        var memoryKiB = BinaryPrimitives.ReadInt32LittleEndian(span[MemoryOffset..]);
        if (memoryKiB < MemoryMinKiB || memoryKiB > MemoryMaxKiB)
        {
            error = SealFormatError.KdfMemory;
            return false;
        }

        var iterations = BinaryPrimitives.ReadInt32LittleEndian(span[IterationsOffset..]);
        if (iterations < IterationsMin || iterations > IterationsMax)
        {
            error = SealFormatError.KdfIterations;
            return false;
        }

        var parallelism = span[ParallelismOffset];
        if (parallelism != ParallelismOnly)
        {
            error = SealFormatError.KdfParallelism;
            return false;
        }

        parameters = new SealKdfParams(memoryKiB, iterations, parallelism);
        error = SealFormatError.None;
        return true;
    }






    public byte[] DeriveKey(string password, ReadOnlySpan<byte> salt)
    {
        if (!IsInitialised)
            throw new InvalidOperationException("kdf parameters were never initialised");
        if (salt.Length != SaltSize)
            throw new ArgumentException($"salt must be {SaltSize} bytes", nameof(salt));

        return CryptoService.DeriveKeyArgon2idUncached(password, salt.ToArray(), Iterations, MemoryKiB);
    }
}
