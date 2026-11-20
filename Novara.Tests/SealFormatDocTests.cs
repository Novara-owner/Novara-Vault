using System.Text.Json;
using System.Text.RegularExpressions;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealFormatDocTests
{
    private const string DocumentName = "Novara 私密空间容器格式规范.md";

    private static readonly string DocumentPath = Locate();

    private static string Locate()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Novara.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var devPath = Path.Combine(root!.FullName, "_docs", DocumentName);

        var publicPath = Path.Combine(root!.FullName, "docs", "seal-container-format.md");
        var path = File.Exists(devPath) ? devPath : publicPath;
        Assert.True(File.Exists(path), $"the published format document is missing: {devPath} / {publicPath}");
        return path;
    }

    private static string Document() => File.ReadAllText(DocumentPath).Replace("\r\n", "\n");


    private sealed record DocRow(string Section, string Field, int Offset, int Size);

    private static IReadOnlyList<DocRow> Rows(string text)
    {
        var rows = new List<DocRow>();
        var section = "";
        var sectionPattern = new Regex(@"^#{2,3}\s+(\d+(?:\.\d+)?)\b", RegexOptions.Multiline);

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();

            var heading = sectionPattern.Match(trimmed);
            if (heading.Success)
            {
                section = heading.Groups[1].Value;
                continue;
            }

            if (!trimmed.StartsWith('|')) continue;

            var cells = trimmed.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length < 3) continue;

            if (!int.TryParse(cells[0], out var offset)) continue;
            if (!int.TryParse(cells[1], out var size)) continue;
            if (cells[2].Length == 0) continue;

            rows.Add(new DocRow(section, cells[2], offset, size));
        }

        return rows;
    }

    private static void AssertRow(IReadOnlyList<DocRow> rows, string section, string field, int offset, int size)
    {
        var row = rows.FirstOrDefault(r =>
            string.Equals(r.Section, section, StringComparison.Ordinal)
            && string.Equals(r.Field, field, StringComparison.Ordinal));

        Assert.True(row is not null, $"the document has no {field} row in section {section}");
        Assert.Equal(offset, row!.Offset);
        Assert.Equal(size, row.Size);
    }

    private static JsonElement Manifest()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Novara.slnx"))) root = root.Parent;
        var path = Path.Combine(root!.FullName, "Novara.Tests", "Fixtures", "seal-container-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }



    [Fact]
    public void ContainerHeaderTable_MatchesSealFormat()
    {
        var rows = Rows(Document());

        AssertRow(rows, "2.1", "magic", SealFormat.MagicOffset, SealFormat.MagicContainer.Length);
        AssertRow(rows, "2.1", "version", SealFormat.VersionOffset, 1);
        AssertRow(rows, "2.1", "algoId", SealFormat.AlgoIdOffset, 1);
        AssertRow(rows, "2.1", "flags", SealFormat.FlagsOffset, 1);
        AssertRow(rows, "2.1", "reserved", SealFormat.ReservedOffset, 1);
        AssertRow(rows, "2.1", "itemId", SealFormat.ItemIdOffset, SealFormat.ItemIdSize);
        AssertRow(rows, "2.1", "plaintextLength", SealFormat.PlaintextLengthOffset, 8);
        AssertRow(rows, "2.1", "sealedAtUtc", SealFormat.SealedAtUtcOffset, 8);
        AssertRow(rows, "2.1", "chunkSize", SealFormat.ChunkSizeOffset, 4);
        AssertRow(rows, "2.1", "keySlot", SealFormat.KeySlotOffset, SealFormat.KeySlotSize);
        AssertRow(rows, "2.1", "reserved2", SealFormat.TrailerReservedOffset, SealFormat.TrailerReservedSize);


        Assert.Equal(SealFormat.HeaderSize, SealFormat.TrailerReservedOffset + SealFormat.TrailerReservedSize);
        Assert.Equal(SealFormat.AuthenticatedPrefixSize, SealFormat.KeySlotOffset);
    }

    [Fact]
    public void KeySlotAndBlockTables_MatchSealFormat()
    {
        var rows = Rows(Document());

        AssertRow(rows, "2.2", "slotNonce", SealKeySlot.NonceOffset, SealFormat.NonceSize);
        AssertRow(rows, "2.2", "wrappedKey", SealKeySlot.WrappedKeyOffset, SealFormat.KeySize);
        AssertRow(rows, "2.2", "slotTag", SealKeySlot.TagOffset, SealFormat.TagSize);
        AssertRow(rows, "2.2", "slotReserved", SealKeySlot.ReservedOffset, SealKeySlot.ReservedSize);
        Assert.Equal(SealFormat.KeySlotSize, SealKeySlot.ReservedOffset + SealKeySlot.ReservedSize);

        AssertRow(rows, "2.3", "payloadLen", 0, SealFormat.BlockLengthPrefixSize);


        AssertRow(rows, "2.3", "itemId", 0, SealFormat.ItemIdSize);
        AssertRow(rows, "2.3", "blockIndex", SealFormat.ItemIdSize, 4);
        AssertRow(rows, "2.3", "isFinal", SealFormat.ItemIdSize + 4, 1);
        Assert.Equal(SealFormat.BlockAadSize, SealFormat.ItemIdSize + 4 + 1);
    }

    [Fact]
    public void KeyWrapKdfIndexAndBundleTables_MatchTheWireConstants()
    {
        var rows = Rows(Document());

        AssertRow(rows, "3", "magic", 0, 8);
        AssertRow(rows, "3", "version", SealKeyWrap.VersionOffset, 1);
        AssertRow(rows, "3", "kdfId", SealKeyWrap.KdfIdOffset, 1);
        AssertRow(rows, "3", "flags", SealKeyWrap.FlagsOffset, SealKeyWrap.FlagsSize);
        AssertRow(rows, "3", "kdfParams", SealKeyWrap.KdfParamsOffset, SealKdfParams.Size);
        AssertRow(rows, "3", "salt", SealKeyWrap.SaltOffset, SealKdfParams.SaltSize);
        AssertRow(rows, "3", "nonce", SealKeyWrap.NonceOffset, SealFormat.NonceSize);
        AssertRow(rows, "3", "wrappedVmk", SealKeyWrap.WrappedKeyOffset, SealFormat.KeySize);
        AssertRow(rows, "3", "tag", SealKeyWrap.TagOffset, SealFormat.TagSize);
        Assert.Equal(SealKeyWrap.HeaderSize, SealKeyWrap.SaltOffset + SealKdfParams.SaltSize);
        Assert.Equal(SealKeyWrap.FileSize, SealKeyWrap.TagOffset + SealFormat.TagSize);

        AssertRow(rows, "3.1", "memoryKiB", SealKdfParams.MemoryOffset, 4);
        AssertRow(rows, "3.1", "iterations", SealKdfParams.IterationsOffset, 4);
        AssertRow(rows, "3.1", "parallelism", SealKdfParams.ParallelismOffset, 1);
        AssertRow(rows, "3.1", "reserved", SealKdfParams.ReservedOffset, SealKdfParams.ReservedSize);
        Assert.Equal(SealKdfParams.Size, SealKdfParams.ReservedOffset + SealKdfParams.ReservedSize);

        AssertRow(rows, "4", "magic", 0, 8);
        AssertRow(rows, "4", "version", 8, 1);
        AssertRow(rows, "4", "itemCount", 9, 4);
        AssertRow(rows, "4", "nonce", 13, SealFormat.NonceSize);
        Assert.Equal(SealIndex.HeaderSize, 13 + SealFormat.NonceSize);

        AssertRow(rows, "5.1", "magic", SealBundleWriter.MagicOffset, 8);
        AssertRow(rows, "5.1", "version", SealBundleWriter.VersionOffset, 1);
        AssertRow(rows, "5.1", "kind", SealBundleWriter.KindOffset, 1);
        AssertRow(rows, "5.1", "flags", SealBundleWriter.FlagsOffset, 2);
        AssertRow(rows, "5.1", "bundleId", SealBundleWriter.BundleIdOffset, 16);
        AssertRow(rows, "5.1", "itemCount", SealBundleWriter.ItemCountOffset, 4);
        AssertRow(rows, "5.1", "indexLength", SealBundleWriter.IndexLengthOffset, 4);
        AssertRow(rows, "5.1", "bodyLength", SealBundleWriter.BodyLengthOffset, 8);
        AssertRow(rows, "5.1", "createdUtc", SealBundleWriter.CreatedUtcOffset, 8);
        AssertRow(rows, "5.1", "appVersion", SealBundleWriter.AppVersionOffset, 4);
        AssertRow(rows, "5.1", "reserved", SealBundleWriter.ReservedOffset, SealBundleWriter.ReservedSize);
        Assert.Equal(SealBundleWriter.HeaderSize, SealBundleWriter.ReservedOffset + SealBundleWriter.ReservedSize);
    }







    [Fact]
    public void Document_CarriesNoInternalIssueIds()
    {
        var text = Document();

        Assert.False(Regex.IsMatch(text, @"\bV\d{1,2}-\d+"), "the document carries an internal issue id");
        Assert.False(Regex.IsMatch(text, @"\bP\d{1,2}\b"), "the document carries an internal phase id");
        Assert.False(Regex.IsMatch(text, @"§\s*17"), "the document references an internal chapter");
        Assert.False(Regex.IsMatch(text, @"TODO|TBD"), "the document carries a placeholder");
    }





    [Fact]
    public void HexSamples_MatchTheFrozenVector()
    {
        var text = Document();
        var manifest = Manifest();

        var samples = Regex.Matches(text, @"^[0-9a-f]{256,}$", RegexOptions.Multiline)
            .Select(m => Convert.FromHexString(m.Value))
            .ToArray();

        Assert.Equal(2, samples.Length);

        var empty = samples.First(s => s.Length == SealFormat.HeaderSize);
        Assert.Equal(
            manifest.GetProperty("boundaries")[0].GetProperty("header").GetString(),
            Convert.ToBase64String(empty));

        var oneByte = samples.First(s => s.Length == 161);
        Assert.Equal(161, oneByte.Length);
        Assert.Equal(
            manifest.GetProperty("boundaries")[1].GetProperty("header").GetString(),
            Convert.ToBase64String(oneByte[..SealFormat.HeaderSize]));


        Assert.Equal(29u, BitConverter.ToUInt32(oneByte, SealFormat.HeaderSize));
        Assert.All(oneByte.AsSpan(SealFormat.HeaderSize + 4, SealFormat.NonceSize).ToArray(), b => Assert.Equal(0, b));
        Assert.NotEqual(0, oneByte[^1]);


        foreach (var entry in manifest.GetProperty("boundaries").EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString();
            var length = entry.GetProperty("plaintextLength").GetInt32();
            var blocks = entry.GetProperty("blockCount").GetInt32();
            var expected = SealFormat.HeaderSize
                           + blocks * (SealFormat.BlockLengthPrefixSize + SealFormat.NonceSize + SealFormat.TagSize)
                           + length;

            Assert.Equal(entry.GetProperty("containerLength").GetInt32(), expected);
            Assert.Contains($"| {name} | {length} | {blocks} | {expected} |", text, StringComparison.Ordinal);
        }
    }
}
