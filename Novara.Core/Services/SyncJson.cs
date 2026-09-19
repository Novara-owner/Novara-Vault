using System.Text.Json;
using Novara.Models;

namespace Novara.Services;


public static class SyncJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string SerializeState(SyncState state)
        => JsonSerializer.Serialize(state ?? throw new ArgumentNullException(nameof(state)), Options);


    public static SyncState DeserializeState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new SyncState();
        try { return JsonSerializer.Deserialize<SyncState>(json, Options) ?? new SyncState(); }
        catch (JsonException) { return new SyncState(); }
    }

    public static string SerializeKeyWrap(SyncKeyWrapRecord record)
        => JsonSerializer.Serialize(record ?? throw new ArgumentNullException(nameof(record)), Options);


    public static SyncKeyWrapRecord DeserializeKeyWrap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("keywrap record is empty");
        try
        {
            return JsonSerializer.Deserialize<SyncKeyWrapRecord>(json, Options)
                ?? throw new InvalidDataException("keywrap record is empty");
        }
        catch (JsonException) { throw new InvalidDataException("keywrap record is not valid JSON"); }
    }







    public static NovaraDatabase DeserializeDatabase(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("payload is empty");
        try
        {
            return JsonSerializer.Deserialize<NovaraDatabase>(json, Options)
                ?? throw new InvalidDataException("payload is empty");
        }
        catch (JsonException) { throw new InvalidDataException("payload is not valid JSON"); }
    }
}
