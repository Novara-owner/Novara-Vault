using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Novara.Services;








public sealed class SyncEnvelope
{

    public const int CurrentSyncVersion = 1;


    public const int CurrentCryptoVersion = 4;

    public int Sync { get; set; } = CurrentSyncVersion;
    public int Crypto { get; set; } = CurrentCryptoVersion;
    public string Space { get; set; } = "";
    public long Version { get; set; }
    public long Base { get; set; }
    public string Device { get; set; } = "";
    public string Ts { get; set; } = "";
    public string PayloadSha256 { get; set; } = "";
    public string Mac { get; set; } = "";


    public string Payload { get; set; } = "";
}


public static class SyncEnvelopeCodec
{

    public const string MacInfo = "novara-sync-mac";


    private static readonly string[] CanonicalFields =
        { "sync", "crypto", "space", "version", "base", "device", "ts", "payloadSha256" };


    public static string UtcStamp(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);


    public static bool TryParseStamp(string? text, out DateTimeOffset value)
        => DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out value);





    public static byte[] DeriveMacKey(string spaceKeyBase64)
    {
        if (!SyncKeyWrap.IsWellFormedSpaceKey(spaceKeyBase64))
            throw new ArgumentException("space key must be valid base64 text", nameof(spaceKeyBase64));

        return HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(spaceKeyBase64), 32,
            salt: null, info: Encoding.UTF8.GetBytes(MacInfo));
    }


    public static string HashPayload(byte[] payloadBytes)
        => Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();





    public static byte[] Canonical(SyncEnvelope envelope)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false, SkipValidation = true }))
        {
            w.WriteStartObject();
            w.WriteNumber(CanonicalFields[0], envelope.Sync);
            w.WriteNumber(CanonicalFields[1], envelope.Crypto);
            w.WriteString(CanonicalFields[2], envelope.Space);
            w.WriteNumber(CanonicalFields[3], envelope.Version);
            w.WriteNumber(CanonicalFields[4], envelope.Base);
            w.WriteString(CanonicalFields[5], envelope.Device);
            w.WriteString(CanonicalFields[6], envelope.Ts);
            w.WriteString(CanonicalFields[7], envelope.PayloadSha256);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }





    public static void Seal(SyncEnvelope envelope, string spaceKeyBase64)
    {
        if (!TryDecodePayload(envelope, out var payloadBytes))
            throw new InvalidDataException("sync envelope payload is not valid base64");

        envelope.PayloadSha256 = HashPayload(payloadBytes);
        using var hmac = new HMACSHA256(DeriveMacKey(spaceKeyBase64));
        envelope.Mac = Convert.ToBase64String(hmac.ComputeHash(Canonical(envelope)));
    }





    public static bool Verify(SyncEnvelope envelope, string spaceKeyBase64)
    {
        if (string.IsNullOrEmpty(envelope.Mac)) return false;
        if (!TryDecodePayload(envelope, out var payloadBytes)) return false;
        if (!string.Equals(HashPayload(payloadBytes), envelope.PayloadSha256, StringComparison.OrdinalIgnoreCase)) return false;

        byte[] expected;
        byte[] actual;
        try
        {
            using var hmac = new HMACSHA256(DeriveMacKey(spaceKeyBase64));
            expected = hmac.ComputeHash(Canonical(envelope));
            actual = Convert.FromBase64String(envelope.Mac);
        }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }

        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }


    public static byte[] DecodePayload(SyncEnvelope envelope)
        => !TryDecodePayload(envelope, out var bytes)
            ? throw new InvalidDataException("sync envelope payload is not valid base64")
            : bytes;

    private static bool TryDecodePayload(SyncEnvelope envelope, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrEmpty(envelope.Payload)) return false;
        try { bytes = Convert.FromBase64String(envelope.Payload); return bytes.Length > 0; }
        catch (FormatException) { return false; }
    }


    public static string Serialize(SyncEnvelope envelope)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteNumber(CanonicalFields[0], envelope.Sync);
            w.WriteNumber(CanonicalFields[1], envelope.Crypto);
            w.WriteString(CanonicalFields[2], envelope.Space);
            w.WriteNumber(CanonicalFields[3], envelope.Version);
            w.WriteNumber(CanonicalFields[4], envelope.Base);
            w.WriteString(CanonicalFields[5], envelope.Device);
            w.WriteString(CanonicalFields[6], envelope.Ts);
            w.WriteString(CanonicalFields[7], envelope.PayloadSha256);
            w.WriteString("mac", envelope.Mac);
            w.WriteString("payload", envelope.Payload);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }







    public static SyncEnvelope Deserialize(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException e) { throw new InvalidDataException("sync envelope is not valid JSON", e); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("sync envelope must be a JSON object");

            var envelope = new SyncEnvelope
            {
                Sync = ReadInt(root, "sync"),
                Crypto = ReadInt(root, "crypto"),
                Space = ReadString(root, "space"),
                Version = ReadLong(root, "version"),
                Base = ReadLong(root, "base"),
                Device = ReadString(root, "device"),
                Ts = ReadString(root, "ts"),
                PayloadSha256 = ReadString(root, "payloadSha256"),
                Mac = ReadString(root, "mac"),
                Payload = ReadString(root, "payload"),
            };
            if (envelope.Sync != SyncEnvelope.CurrentSyncVersion)
                throw new InvalidDataException($"unsupported sync envelope version: {envelope.Sync}");
            if (envelope.Version < 1) throw new InvalidDataException("sync envelope version must be >= 1");
            if (envelope.Base < 0) throw new InvalidDataException("sync envelope base must be >= 0");
            return envelope;
        }
    }

    private static int ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i : throw new InvalidDataException($"sync envelope field '{name}' is missing or not an integer");

    private static long ReadLong(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var i)
            ? i : throw new InvalidDataException($"sync envelope field '{name}' is missing or not an integer");

    private static string ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()! : throw new InvalidDataException($"sync envelope field '{name}' is missing or not a string");
}
