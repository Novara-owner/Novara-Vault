using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Novara.Models;

namespace Novara.Services;


public enum SealJournalStatus
{

    Ok = 0,


    Unreadable,


    VersionUnsupported,
}






















public sealed class SealJournal
{

    public const int HeaderSize = 8 + 1 + 4 + SealFormat.NonceSize;


    public const int MaxFileBytes = 16 * 1024 * 1024;






    public const int MaxRecords = 10_000;

    public const byte CurrentVersion = 1;






    private static ReadOnlySpan<byte> Magic => "NSEALJNL"u8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,


        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly List<SealJournalRecord> _records;

    public IReadOnlyList<SealJournalRecord> Records => _records;

    public SealJournalStatus Status { get; }


    public SealCorruptReason? Reason { get; }


    public bool HasMalformedRecords => _records.Any(r => r.IsMalformed);

    private SealJournal(List<SealJournalRecord> records, SealJournalStatus status, SealCorruptReason? reason)
    {
        _records = records;
        Status = status;
        Reason = reason;
    }


    public static SealJournal Empty { get; } = new([], SealJournalStatus.Ok, null);


    public IReadOnlyList<SealJournalRecord> ForOperation(string opId)
        => _records.Where(r => string.Equals(r.OpId, opId, StringComparison.Ordinal)).ToList();






    public SealJournal Append(SealJournalRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (Status != SealJournalStatus.Ok)
            throw new InvalidOperationException("the journal is not readable; it must be resolved before anything is appended");

        if (!SealJournalRecord.IsWellFormed(record, out var reason))
            throw new ArgumentException(reason, nameof(record));

        if (_records.Any(r => string.Equals(r.OpId, record.OpId, StringComparison.Ordinal)
                              && string.Equals(r.Phase, record.Phase, StringComparison.Ordinal)))
        {
            throw new ArgumentException("this operation already has a record for that phase", nameof(record));
        }

        var copy = new List<SealJournalRecord>(_records) { record };
        return new SealJournal(copy, SealJournalStatus.Ok, null);
    }






    public SealJournal Remove(string opId)
    {
        ArgumentException.ThrowIfNullOrEmpty(opId);

        var copy = new List<SealJournalRecord>(_records);
        if (copy.RemoveAll(r => string.Equals(r.OpId, opId, StringComparison.Ordinal)) == 0) return this;

        return new SealJournal(copy, Status, Reason);
    }







    public SealJournal RemoveMalformedRecords()
    {
        var copy = new List<SealJournalRecord>(_records);
        if (copy.RemoveAll(r => r.IsMalformed) == 0) return this;
        return new SealJournal(copy, Status, Reason);
    }












    public static SealJournal LoadRequiredIfPresent(string path, byte[] vmk)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(vmk);
        if (!File.Exists(path))
        {


            try
            {
                using var probe = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                 FileShare.ReadWrite | FileShare.Delete);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Unreadable(SealCorruptReason.IoUnavailable);
            }
        }
        try { return Load(path, vmk); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Unreadable(SealCorruptReason.IoUnavailable); }
    }

    public static SealJournal Load(string path, byte[] vmk)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(vmk);

        if (vmk.Length != SealFormat.KeySize)
            throw new ArgumentException($"vmk must be {SealFormat.KeySize} bytes", nameof(vmk));

        if (!File.Exists(path)) return Empty;

        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes) return Unreadable(SealCorruptReason.LengthMismatch);
        if (info.Length < HeaderSize + SealFormat.TagSize) return Unreadable(SealCorruptReason.Truncated);

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaxFileBytes) return Unreadable(SealCorruptReason.LengthMismatch);
        if (bytes.Length < HeaderSize + SealFormat.TagSize) return Unreadable(SealCorruptReason.Truncated);
        if (!bytes.AsSpan(0, 8).SequenceEqual(Magic)) return Unreadable(SealCorruptReason.HeaderInvalid);

        var version = bytes[8];
        if (version == 0 || version > CurrentVersion)
            return new SealJournal([], SealJournalStatus.VersionUnsupported, null);

        var recordCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(9, 4));
        if (recordCount < 0 || recordCount > MaxRecords) return Unreadable(SealCorruptReason.LengthMismatch);

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
            return Unreadable(SealCorruptReason.Authentication);
        }

        try
        {
            List<SealJournalRecord>? records;
            try
            {
                records = JsonSerializer.Deserialize<List<SealJournalRecord>>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return Unreadable(SealCorruptReason.HeaderInvalid);
            }

            if (records is null) return Unreadable(SealCorruptReason.HeaderInvalid);
            if (records.Count != recordCount) return Unreadable(SealCorruptReason.LengthMismatch);



            foreach (var record in records)
            {
                if (SealJournalRecord.IsWellFormed(record, out var reason)) continue;

                record.IsMalformed = true;
                record.MalformedReason = reason;
            }

            return new SealJournal(records, SealJournalStatus.Ok, null);
        }
        finally
        {


            CryptographicOperations.ZeroMemory(json);
        }
    }










    public void Save(string path, byte[] vmk) => SaveCore(path, vmk, null);




    internal void SaveWithNonce(string path, byte[] vmk, byte[] nonce) => SaveCore(path, vmk, nonce);

    private void SaveCore(string path, byte[] vmk, byte[]? fixedNonce)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(vmk);

        if (vmk.Length != SealFormat.KeySize)
            throw new ArgumentException($"vmk must be {SealFormat.KeySize} bytes", nameof(vmk));

        if (Status != SealJournalStatus.Ok)
            throw new InvalidOperationException("refusing to overwrite a journal that could not be read");

        if (HasMalformedRecords)
            throw new InvalidOperationException("refusing to save: the journal holds records that failed validation");

        if (_records.Count > MaxRecords)
            throw new InvalidOperationException($"the journal cannot hold more than {MaxRecords} records");

        var json = SerializePayload()
                   ?? throw new InvalidOperationException("journal serialization did not settle; nothing was written");

        try
        {
            var header = new byte[HeaderSize];
            Magic.CopyTo(header);
            header[8] = CurrentVersion;
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(9, 4), _records.Count);

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
        => StableJson.SerializeToUtf8BytesStable(new List<SealJournalRecord>(_records), JsonOptions)
           ?? throw new InvalidOperationException("journal serialization did not settle");

    private static SealJournal Unreadable(SealCorruptReason reason) => new([], SealJournalStatus.Unreadable, reason);
}
