using System.Text.Json;
using Novara.Models;

namespace Novara.Services;

















public static class SyncFieldPolicy
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };


    public static NovaraDatabase CloneForSync(NovaraDatabase source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        var json = JsonSerializer.SerializeToUtf8Bytes(source, Options);
        var clone = JsonSerializer.Deserialize<NovaraDatabase>(json, Options) ?? new NovaraDatabase();
        clone.AppSettings = BuildRoamingSettings(source.AppSettings);
        return clone;
    }


    public static AppSettings BuildRoamingSettings(AppSettings? source)
    {
        var roaming = new AppSettings();
        if (source is null) return roaming;
        roaming.AppLanguage = source.AppLanguage;
        roaming.Theme = source.Theme;
        return roaming;
    }






    public static AppSettings ApplyRoamingSettings(AppSettings local, AppSettings? remote)
    {
        if (local is null) throw new ArgumentNullException(nameof(local));
        if (remote is null) return local;
        local.AppLanguage = remote.AppLanguage;
        local.Theme = remote.Theme;
        return local;
    }
}
