using System.Security.Cryptography;
using System.Text.Json;

namespace Novara.Services;

public static class SyncCredentialVault
{
    private const string FileName = "sync-credentials.dat";

    private static string VaultPath => Path.Combine(
        Path.GetDirectoryName(NovaraStore.DefaultFilePath)!, FileName);

    private static string SlotFor(string spaceId) => "sync-device|" + spaceId;








    private static Dictionary<string, string> Load(out bool readFailed)
    {
        readFailed = false;
        try
        {
            if (!File.Exists(VaultPath)) return new Dictionary<string, string>();
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(VaultPath), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new Dictionary<string, string>();
        }
        catch
        {
            readFailed = true;
            return new Dictionary<string, string>();
        }
    }


    private static Dictionary<string, string> Load() => Load(out _);





    private static bool Save(Dictionary<string, string> map)
    {
        try
        {
            var plain = JsonSerializer.SerializeToUtf8Bytes(map);
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            var tmp = VaultPath + ".tmp";
            File.WriteAllBytes(tmp, encrypted);
            File.Move(tmp, VaultPath, true);
            return true;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(VaultPath + ".error.txt", ex.ToString()); } catch { }
            return false;
        }
    }




    public static bool Store(string spaceId, string token)
    {
        if (string.IsNullOrEmpty(spaceId) || string.IsNullOrEmpty(token)) return false;
        var map = Load(out var readFailed);
        if (readFailed) return false;
        map[SlotFor(spaceId)] = token;
        return Save(map);
    }


    public static string? TryGet(string spaceId) => TryGet(spaceId, out _);





    public static string? TryGet(string spaceId, out bool readFailed)
    {
        readFailed = false;
        if (string.IsNullOrEmpty(spaceId)) return null;
        var map = Load(out readFailed);
        return map.TryGetValue(SlotFor(spaceId), out var token) && !string.IsNullOrEmpty(token)
            ? token
            : null;
    }










    public static bool Clear(string spaceId)
    {
        if (string.IsNullOrEmpty(spaceId)) return false;
        var map = Load(out var readFailed);
        if (readFailed)
        {
            try { File.Delete(VaultPath); return true; }
            catch { return false; }
        }
        return !map.Remove(SlotFor(spaceId)) || Save(map);
    }
}
