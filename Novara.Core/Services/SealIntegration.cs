using System.Security.Cryptography;
using Novara.Models;

namespace Novara.Services;


public enum SealBackupStatus
{
    Ok = 0,


    BackupFailed,


    Corrupt,

    WrongPassword,
    IoError,
}

















public static class SealIntegration
{




    public static async Task<SealBackupStatus> ExportBackupWithSealAsync(
        NovaraStore store,
        string targetPath,
        bool includeFilePathEntries,
        string backupPassword,
        string spacePassword,
        SealSpacePaths paths,
        byte[] vmk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrEmpty(targetPath);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(vmk);

        var temporary = targetPath + ".backup.tmp.novaenc";

        try
        {
            if (!store.ExportBackupEncrypted(temporary, backupPassword, includeFilePathEntries))
                return SealBackupStatus.BackupFailed;

            var backup = await File.ReadAllBytesAsync(temporary, cancellationToken).ConfigureAwait(false);

            var index = SealIndex.LoadInitialized(paths.Index, vmk);
            var items = index.Items
                .Select(item => new SealBundleItem(item.Id, paths.ItemPath(item.Id)))
                .ToList();

            await SealBundleWriter.WriteAsync(
                    targetPath,
                    new SealBundleOptions(
                        vmk, index, items, spacePassword,
                        SealBundleWriter.KindItemsWithBackup,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds(), AppVersionCode(), backup),
                    cancellationToken)
                .ConfigureAwait(false);

            return SealBackupStatus.Ok;
        }
        catch (SealCorruptException)
        {
            return SealBackupStatus.Corrupt;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SealBackupStatus.IoError;
        }
        finally
        {
            TryDelete(temporary);
        }
    }





    public static SealStoreOutcome ClearPrivateSpace(SealSpacePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new SealSpaceStore(paths).DeleteAll();
    }





    public static bool ExportBackupWithoutSeal(
        NovaraStore store, string targetPath, bool includeFilePathEntries)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.ExportBackup(targetPath, includeFilePathEntries);
    }

    private static int AppVersionCode()
    {
        var version = typeof(SealIntegration).Assembly.GetName().Version ?? new Version(1, 0, 0);
        return version.Major * 1000 + version.Minor;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {

        }
    }
}
