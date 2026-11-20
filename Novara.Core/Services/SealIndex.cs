using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Novara.Models;

namespace Novara.Services;
















public sealed class SealIndex
{

    public const int HeaderSize = 8 + 1 + 4 + SealFormat.NonceSize;


    public const int MaxFileBytes = 64 * 1024 * 1024;

    public const int MaxItems = 1_000_000;

    public const byte CurrentVersion = 1;

    private static ReadOnlySpan<byte> Magic => "NSEALIDX"u8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,



        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly List<SealedItem> _items;
    private readonly Dictionary<string, SealedItem> _byId;

    public IReadOnlyList<SealedItem> Items => _items;


    public long ObservedMaxUtc { get; }

    private SealIndex(List<SealedItem> items, long observedMaxUtc)
    {
        _items = items;
        ObservedMaxUtc = observedMaxUtc;

        _byId = new Dictionary<string, SealedItem>(items.Count, StringComparer.Ordinal);
        foreach (var item in items) _byId[item.Id] = item;
    }


    public static SealIndex Empty { get; } = new([], 0);

    public SealedItem? Find(string id) => _byId.TryGetValue(id, out var item) ? item : null;

    public SealIndex WithItem(SealedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!SealedItem.IsWellFormed(item, out var reason))
            throw new ArgumentException(reason, nameof(item));
        if (_byId.ContainsKey(item.Id))
            throw new ArgumentException("an item with this id is already in the index", nameof(item));

        var copy = new List<SealedItem>(_items) { item };
        return new SealIndex(copy, ObservedMaxUtc);
    }





    public SealIndex WithItems(IReadOnlyList<SealedItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) return this;

        var copy = new List<SealedItem>(_items.Count + items.Count);
        copy.AddRange(_items);
        var seen = new HashSet<string>(_byId.Keys, StringComparer.Ordinal);

        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!SealedItem.IsWellFormed(item, out var reason))
                throw new ArgumentException(reason, nameof(items));
            if (!seen.Add(item.Id))
                throw new ArgumentException("an item with this id is already in the index", nameof(items));
            copy.Add(item);
        }

        return new SealIndex(copy, ObservedMaxUtc);
    }


    public SealIndex WithoutItem(string id)
    {
        var copy = new List<SealedItem>(_items);
        if (copy.RemoveAll(i => string.Equals(i.Id, id, StringComparison.Ordinal)) == 0) return this;

        return new SealIndex(copy, ObservedMaxUtc);
    }


    public SealIndex WithObservedMax(long observedMaxUtc)
        => observedMaxUtc <= ObservedMaxUtc ? this : new SealIndex(new List<SealedItem>(_items), observedMaxUtc);



    public SealIndex WithObservedMaxReset(long observedMaxUtc)
        => new(new List<SealedItem>(_items), Math.Max(0, observedMaxUtc));








    public static SealIndex Load(string path, byte[] vmk)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(vmk);

        if (vmk.Length != SealFormat.KeySize)
            throw new ArgumentException($"vmk must be {SealFormat.KeySize} bytes", nameof(vmk));

        if (!File.Exists(path)) return Empty;

        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes)
            throw new SealCorruptException(SealCorruptReason.LengthMismatch);
        if (info.Length < HeaderSize + SealFormat.TagSize)
            throw new SealCorruptException(SealCorruptReason.Truncated);

        var bytes = File.ReadAllBytes(path);


        if (bytes.Length > MaxFileBytes)
            throw new SealCorruptException(SealCorruptReason.LengthMismatch);
        if (bytes.Length < HeaderSize + SealFormat.TagSize)
            throw new SealCorruptException(SealCorruptReason.Truncated);
        if (!bytes.AsSpan(0, 8).SequenceEqual(Magic))
            throw new SealCorruptException(SealCorruptReason.HeaderInvalid);

        var version = bytes[8];
        if (version == 0 || version > CurrentVersion)
            throw new SealVersionUnsupportedException(version);

        var itemCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(9, 4));
        if (itemCount < 0 || itemCount > MaxItems)
            throw new SealCorruptException(SealCorruptReason.LengthMismatch);

        var nonce = bytes.AsSpan(13, SealFormat.NonceSize).ToArray();
        var sealedBytes = bytes.AsSpan(HeaderSize).ToArray();
        var aad = bytes.AsSpan(8, 5).ToArray();

        byte[] json;
        try
        {
            json = CryptoService.DecryptGcmWithNonce(sealedBytes, vmk, nonce, aad);
        }
        catch (CryptographicException)
        {
            throw new SealCorruptException(SealCorruptReason.Authentication);
        }

        try
        {
            return FromPayload(json, itemCount);
        }
        finally
        {


            CryptographicOperations.ZeroMemory(json);
        }
    }








    public static SealIndex LoadInitialized(string path, byte[] vmk)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
            throw new SealCorruptException(SealCorruptReason.Truncated);
        return Load(path, vmk);
    }





    internal static SealIndex FromPayload(byte[] json, int expectedItemCount)
    {
        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(json, JsonOptions);
        }
        catch (JsonException)
        {
            throw new SealCorruptException(SealCorruptReason.HeaderInvalid);
        }

        if (payload is null || payload.Items is null)
            throw new SealCorruptException(SealCorruptReason.HeaderInvalid);
        if (payload.Items.Count != expectedItemCount)
            throw new SealCorruptException(SealCorruptReason.LengthMismatch);
        if (payload.ObservedMaxUtc < 0)
            throw new SealCorruptException(SealCorruptReason.HeaderInvalid);



        var observedMax = Math.Min(payload.ObservedMaxUtc, SealFormat.MaxSealedAtUtc);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in payload.Items)
        {
            if (!SealedItem.IsWellFormed(item, out _))
                throw new SealCorruptException(SealCorruptReason.HeaderInvalid);
            if (!seen.Add(item.Id))
                throw new SealCorruptException(SealCorruptReason.HeaderInvalid);
        }

        return new SealIndex(payload.Items, observedMax);
    }






    public void Save(string path, byte[] vmk) => SaveCore(path, vmk, null);





    internal void SaveWithNonce(string path, byte[] vmk, byte[] nonce) => SaveCore(path, vmk, nonce);

    private void SaveCore(string path, byte[] vmk, byte[]? fixedNonce)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(vmk);

        if (vmk.Length != SealFormat.KeySize)
            throw new ArgumentException($"vmk must be {SealFormat.KeySize} bytes", nameof(vmk));
        if (_items.Count > MaxItems)
            throw new InvalidOperationException($"the index cannot hold more than {MaxItems} entries");




        var json = SerializePayload()
                   ?? throw new InvalidOperationException("index serialization did not settle; nothing was written");

        try
        {


            if (json.Length > MaxFileBytes - HeaderSize - SealFormat.TagSize)
                throw new SealCorruptException(SealCorruptReason.IndexFull);

            var header = new byte[HeaderSize];
            Magic.CopyTo(header);
            header[8] = CurrentVersion;
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(9, 4), _items.Count);

            var nonce = fixedNonce ?? RandomNumberGenerator.GetBytes(SealFormat.NonceSize);
            if (nonce.Length != SealFormat.NonceSize)
                throw new ArgumentException($"nonce must be {SealFormat.NonceSize} bytes", nameof(fixedNonce));
            var aad = header.AsSpan(8, 5).ToArray();
            var sealedBytes = CryptoService.EncryptGcmWithNonce(json, vmk, nonce, aad);

            var file = new byte[HeaderSize + sealedBytes.Length];
            header.AsSpan(0, 13).CopyTo(file);
            nonce.CopyTo(file.AsSpan(13, SealFormat.NonceSize));
            sealedBytes.CopyTo(file.AsSpan(HeaderSize));

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(file);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
            else File.Move(temporary, path);
        }
        finally
        {

            CryptographicOperations.ZeroMemory(json);
        }
    }






    internal byte[] SerializePayload()
    {
        var payload = new Payload { ObservedMaxUtc = ObservedMaxUtc, Items = new List<SealedItem>(_items) };
        return StableJson.SerializeToUtf8BytesStable(payload, JsonOptions)
               ?? throw new InvalidOperationException("index serialization did not settle");
    }


    private sealed class Payload
    {
        public long ObservedMaxUtc { get; set; }

        public List<SealedItem> Items { get; set; } = [];
    }
}
