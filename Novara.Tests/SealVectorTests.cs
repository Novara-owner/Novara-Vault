using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealVectorTests
{
    private const long SealedAt = 1_790_000_000;
    private const int ChunkSize = SealFormat.DefaultChunkSize;
    private const int PlaintextLength = 3 * 1024 * 1024;

    private const string BinaryName = "seal-container-v1.bin";
    private const string ManifestName = "seal-container-v1.json";

    private static readonly byte[] ItemId = Enumerable.Range(0, SealFormat.ItemIdSize).Select(i => (byte)i).ToArray();
    private static readonly byte[] Vmk = Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0xA0 + i)).ToArray();
    private static readonly byte[] ContentKey = Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0xC0 + i)).ToArray();
    private static readonly byte[] KeySlotNonce = Enumerable.Range(0, SealFormat.NonceSize).Select(i => (byte)(0x10 + i)).ToArray();





    private sealed class CountingNonceSource : ISealNonceSource
    {
        private ulong _counter;

        public void Fill(Span<byte> destination)
        {
            destination.Clear();
            BinaryPrimitives.WriteUInt64LittleEndian(destination, _counter++);
        }
    }

    private static byte[] Plaintext(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i * 31 + 7);
        return data;
    }

    private static Task<byte[]> BuildAsync() => BuildAsync(PlaintextLength);

    private static async Task<byte[]> BuildAsync(int plaintextLength)
    {
        var plain = Plaintext(plaintextLength);
        var header = SealContainerWriter.PrepareHeaderCore(
            ItemId, (ulong)plain.Length, SealedAt, ChunkSize, Vmk, ContentKey, flags: 0, KeySlotNonce);

        var output = new MemoryStream();
        await SealContainerWriter.WriteAsync(
            new MemoryStream(plain), header, ContentKey, output, progress: null, cancellationToken: default,
            nonceSource: new CountingNonceSource());

        return output.ToArray();
    }


    private static readonly (string Name, int Length)[] Boundaries =
    [
        ("empty", 0),
        ("one-byte", 1),
        ("exact-chunk", ChunkSize),
        ("chunk-plus-one", ChunkSize + 1),
    ];

    private static int BlocksFor(int plaintextLength)
        => plaintextLength == 0 ? 0 : (plaintextLength + ChunkSize - 1) / ChunkSize;

    private static DirectoryInfo RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Novara.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return root!;
    }

    private static string FixturePath(string name)
        => Path.Combine(RepositoryRoot().FullName, "Novara.Tests", "Fixtures", name);

    private static JsonElement Manifest()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath(ManifestName)));
        return document.RootElement.Clone();
    }



    [Fact]
    public async Task Container_MatchesThePinnedBytes()
    {
        var produced = await BuildAsync();
        var expected = File.ReadAllBytes(FixturePath(BinaryName));

        Assert.Equal(expected.Length, produced.Length);

        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] != produced[i])
                Assert.Fail($"first byte difference at offset {i}: expected 0x{expected[i]:X2}, got 0x{produced[i]:X2}");
        }
    }

    [Fact]
    public async Task PinnedContainer_DecryptsToThePinnedPlaintext()
    {
        var container = File.ReadAllBytes(FixturePath(BinaryName));
        var collected = new MemoryStream();

        var result = await SealContainerReader.ReadAsync(
            new MemoryStream(container), ContentKey,
            async (block, token) => await collected.WriteAsync(block, token));

        Assert.Equal(PlaintextLength, collected.Length);
        Assert.Equal(Plaintext(PlaintextLength), collected.ToArray());

        var manifest = Manifest();
        Assert.Equal(
            manifest.GetProperty("digests").GetProperty("plaintextSha256").GetString(),
            Convert.ToHexString(result.PlaintextSha256).ToLowerInvariant());
    }

    [Fact]
    public async Task ContainerDigest_MatchesTheManifest()
    {
        var container = await BuildAsync();
        var manifest = Manifest();

        Assert.Equal(
            manifest.GetProperty("digests").GetProperty("containerSha256").GetString(),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(container)).ToLowerInvariant());
        Assert.Equal(
            manifest.GetProperty("shape").GetProperty("containerLength").GetInt32(),
            container.Length);
        Assert.Equal(
            3,
            manifest.GetProperty("shape").GetProperty("blockCount").GetInt32());
    }

    [Fact]
    public void Manifest_ParametersMatchTheGenerator()
    {
        var manifest = Manifest();
        var parameters = manifest.GetProperty("parameters");

        Assert.Equal(Convert.ToBase64String(ItemId), parameters.GetProperty("itemId").GetString());
        Assert.Equal(Convert.ToBase64String(Vmk), parameters.GetProperty("vmk").GetString());
        Assert.Equal(Convert.ToBase64String(ContentKey), parameters.GetProperty("contentKey").GetString());
        Assert.Equal(Convert.ToBase64String(KeySlotNonce), parameters.GetProperty("keySlotNonce").GetString());
        Assert.Equal(SealedAt, parameters.GetProperty("sealedAtUtc").GetInt64());
        Assert.Equal(ChunkSize, parameters.GetProperty("chunkSize").GetInt32());
        Assert.Equal(PlaintextLength, parameters.GetProperty("plaintextLength").GetInt32());
    }






    [Fact]
    public async Task BoundaryVectors_MatchTheManifest()
    {
        var entries = Manifest().GetProperty("boundaries").EnumerateArray().ToArray();
        Assert.Equal(Boundaries.Length, entries.Length);

        for (var i = 0; i < Boundaries.Length; i++)
        {
            var entry = entries[i];
            var (name, length) = Boundaries[i];

            Assert.Equal(name, entry.GetProperty("name").GetString());
            Assert.Equal(length, entry.GetProperty("plaintextLength").GetInt32());

            var container = await BuildAsync(length);
            Assert.Equal(BlocksFor(length), entry.GetProperty("blockCount").GetInt32());
            Assert.Equal(container.Length, entry.GetProperty("containerLength").GetInt32());
            Assert.Equal(
                Convert.ToBase64String(container[..SealFormat.HeaderSize]),
                entry.GetProperty("header").GetString());
        }
    }





    [Fact]
    public void Manifest_HasNoFrameworkOrAssemblyNames()
    {
        var json = File.ReadAllText(FixturePath(ManifestName));

        Assert.DoesNotContain("System.", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Novara", json, StringComparison.Ordinal);
        Assert.DoesNotContain("$type", json, StringComparison.Ordinal);
    }








    [Fact(Skip = "manual regeneration only - see the remarks on this method")]
    public async Task RegenerateFixtures()
    {
        var container = await BuildAsync();
        var plaintext = Plaintext(PlaintextLength);

        File.WriteAllBytes(FixturePath(BinaryName), container);

        var index = new StringBuilder();
        index.AppendLine("{");
        index.AppendLine("  \"format\": \"novaseal-container-v1\",");
        index.AppendLine("  \"note\": \"Frozen vector for .novaseal v1. Bytes live in seal-container-v1.bin. Regenerate only with a deliberate format change.\",");
        index.AppendLine("  \"parameters\": {");
        index.AppendLine($"    \"itemId\": \"{Convert.ToBase64String(ItemId)}\",");
        index.AppendLine($"    \"vmk\": \"{Convert.ToBase64String(Vmk)}\",");
        index.AppendLine($"    \"contentKey\": \"{Convert.ToBase64String(ContentKey)}\",");
        index.AppendLine($"    \"keySlotNonce\": \"{Convert.ToBase64String(KeySlotNonce)}\",");
        index.AppendLine($"    \"sealedAtUtc\": {SealedAt},");
        index.AppendLine($"    \"chunkSize\": {ChunkSize},");
        index.AppendLine($"    \"plaintextLength\": {PlaintextLength},");
        index.AppendLine("    \"plaintextRule\": \"b[i] = (byte)(i * 31 + 7)\",");
        index.AppendLine("    \"blockNonceRule\": \"u64 little-endian block index in bytes 0..7, bytes 8..11 zero\"");
        index.AppendLine("  },");
        index.AppendLine("  \"shape\": {");
        index.AppendLine($"    \"headerSize\": {SealFormat.HeaderSize},");
        index.AppendLine($"    \"blockCount\": {(PlaintextLength + ChunkSize - 1) / ChunkSize},");
        index.AppendLine($"    \"containerLength\": {container.Length}");
        index.AppendLine("  },");
        index.AppendLine("  \"boundaries\": [");

        for (var i = 0; i < Boundaries.Length; i++)
        {
            var (name, length) = Boundaries[i];
            var bytes = await BuildAsync(length);

            index.AppendLine("    {");
            index.AppendLine($"      \"name\": \"{name}\",");
            index.AppendLine($"      \"plaintextLength\": {length},");
            index.AppendLine($"      \"blockCount\": {BlocksFor(length)},");
            index.AppendLine($"      \"containerLength\": {bytes.Length},");
            index.AppendLine($"      \"header\": \"{Convert.ToBase64String(bytes.AsSpan(0, SealFormat.HeaderSize))}\"");
            index.AppendLine(i == Boundaries.Length - 1 ? "    }" : "    },");
        }

        index.AppendLine("  ],");
        index.AppendLine("  \"digests\": {");
        index.AppendLine($"    \"containerSha256\": \"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(container)).ToLowerInvariant()}\",");
        index.AppendLine($"    \"plaintextSha256\": \"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(plaintext)).ToLowerInvariant()}\"");
        index.AppendLine("  }");
        index.AppendLine("}");

        File.WriteAllText(FixturePath(ManifestName), index.ToString(), new UTF8Encoding(false));
    }
}
