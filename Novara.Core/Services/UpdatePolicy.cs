namespace Novara.Services;






public static class UpdatePolicy
{
    public static bool IsNewer(string? latestVersion, string? currentVersion)
    {
        var okL = Version.TryParse(latestVersion?.Trim(), out var latest);
        var okC = Version.TryParse(currentVersion?.Trim(), out var current);
        return okL && okC && latest > current;
    }
}
