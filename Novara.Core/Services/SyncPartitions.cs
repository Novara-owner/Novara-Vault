using System.Security.Cryptography;
using System.Text.Json;
using Novara.Models;

namespace Novara.Services;

public static class SyncPartitions
{

    public static readonly string[] Keys =
    {
        "appSettings", "memoGroups", "memoEntries", "pathBackupItems", "todoCards", "noteCards", "diaryItems",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };


    public static string PidOf(byte[] partitionJson) => Convert.ToHexString(SHA256.HashData(partitionJson));





    public static Dictionary<string, byte[]> ExtractPartitions(byte[] stableFullJson)
    {
        using var doc = JsonDocument.Parse(stableFullJson);
        var root = doc.RootElement;
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var key in Keys)
        {
            var bytes = root.TryGetProperty(key, out var prop)
                ? JsonSerializer.SerializeToUtf8Bytes(prop.Clone(), JsonOptions)
                : JsonSerializer.SerializeToUtf8Bytes(JsonSerializer.Deserialize<JsonElement>("null"), JsonOptions);
            result[key] = bytes;
        }
        return result;
    }










    public static void ApplyPartitions(NovaraDatabase db, IReadOnlyDictionary<string, byte[]> partitions)
    {
        var parsed = new List<(string Key, object Value)>(Keys.Length);
        foreach (var key in Keys)
        {
            if (!partitions.TryGetValue(key, out var bytes)) continue;
            using var doc = JsonDocument.Parse(bytes);
            parsed.Add((key, ParsePartition(key, doc.RootElement)));
        }
        foreach (var (key, value) in parsed) ApplyParsed(db, key, value);



        NovaraStore.NormalizeIdsAndRefs(db);
    }


    private static object ParsePartition(string key, JsonElement payload) => key switch
    {
        "appSettings" => (object)(payload.ValueKind == JsonValueKind.Object
            ? payload.Deserialize<AppSettings>(JsonOptions) ?? new AppSettings()
            : new AppSettings()),
        "memoGroups" => List<Novara.Models.MemoGroup>(payload),
        "memoEntries" => ParseMemoEntries(payload),
        "pathBackupItems" => List<Novara.Models.FilePathEntry>(payload),
        "todoCards" => ParseTodoCards(payload),
        "noteCards" => List<Novara.Models.NoteCard>(payload),
        "diaryItems" => List<Novara.Models.DiaryEntry>(payload),
        _ => throw new InvalidDataException($"unknown sync partition: {key}"),
    };


    private static void ApplyParsed(NovaraDatabase db, string key, object value)
    {
        switch (key)
        {
            case "appSettings":
            {
                var local = db.AppSettings ?? new AppSettings();
                SyncFieldPolicy.ApplyRoamingSettings(local, (AppSettings)value);
                db.AppSettings = local;
                break;
            }
            case "memoGroups": db.MemoGroups = (System.Collections.Generic.List<Novara.Models.MemoGroup>)value; break;
            case "memoEntries": db.MemoEntries = (System.Collections.Generic.List<Novara.Models.MemoEntry>)value; break;
            case "pathBackupItems": db.PathBackupItems = (System.Collections.Generic.List<Novara.Models.FilePathEntry>)value; break;
            case "todoCards": db.TodoCards = (System.Collections.Generic.List<Novara.Models.TodoCard>)value; break;
            case "noteCards": db.NoteCards = (System.Collections.Generic.List<Novara.Models.NoteCard>)value; break;
            case "diaryItems": db.DiaryItems = (System.Collections.Generic.List<Novara.Models.DiaryEntry>)value; break;
        }
    }

    private static object ParseMemoEntries(JsonElement payload)
    {
        var entries = List<Novara.Models.MemoEntry>(payload);
        foreach (var en in entries)
        {
            if (en.Fields == null) en.Fields = new();
            else en.Fields.RemoveAll(f => f == null);
            foreach (var f in en.Fields) { f.Label ??= ""; f.Value ??= ""; }
        }
        return entries;
    }

    private static object ParseTodoCards(JsonElement payload)
    {
        var cards = List<Novara.Models.TodoCard>(payload);
        foreach (var td in cards) NovaraStore.NormalizeTodoStates(td);
        return cards;
    }

    private static List<T> List<T>(JsonElement payload) where T : class
    {
        var result = payload.ValueKind == JsonValueKind.Array
            ? payload.Deserialize<List<T>>(JsonOptions) ?? new List<T>()
            : new List<T>();
        result.RemoveAll(x => x == null);
        return result;
    }
}
