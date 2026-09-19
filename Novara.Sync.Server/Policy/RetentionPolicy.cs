using Novara.Sync.Server.Storage;

namespace Novara.Sync.Server.Policy;




public static class RetentionPolicy
{

    public static long UsedBytes(IReadOnlyList<VersionRecord> versions)
        => versions.Where(v => v.Retained).Sum(v => v.Size);





    public static IReadOnlyList<VersionRecord> PlanEviction(
        IReadOnlyList<VersionRecord> versions,
        long currentVersion,
        int maxVersions,
        long quotaBytes)
    {
        ArgumentNullException.ThrowIfNull(versions);

        var retained = versions.Where(v => v.Retained).OrderBy(v => v.Version).ToList();
        var victims = new List<VersionRecord>();



        var held = retained.Count(v => !v.IsConflict);
        var used = retained.Sum(v => v.Size);
        var versionLimit = Math.Max(1, maxVersions);

        bool OverVersions() => held > versionLimit;
        bool OverQuota() => quotaBytes > 0 && used > quotaBytes;
        bool OverLimit() => OverVersions() || OverQuota();


        foreach (var version in retained.Where(v => !v.IsConflict))
        {
            if (!OverLimit()) break;
            if (version.Version == currentVersion) continue;
            victims.Add(version);
            held--;
            used -= version.Size;
        }





        foreach (var version in retained.Where(v => v.IsConflict))
        {
            if (!OverQuota()) break;
            if (version.Version == currentVersion) continue;
            victims.Add(version);
            used -= version.Size;
        }

        return victims;
    }
}
