using System.IO;
using System.Security.Cryptography;
using Novara.Models;

namespace Novara.Services;

public enum DataFileIntegrity { DigestVerified, EncryptedStructured, Failed }

public static class DatabaseHealth
{




    public static int CountOrphanMemoEntries(NovaraDatabase db)
    {
        if (db == null) return 0;
        var groupIds = new HashSet<Guid>(db.MemoGroups.Select(g => g.Id));
        return db.MemoEntries.Count(e => e.GroupId != null && !groupIds.Contains(e.GroupId.Value));
    }










    public static DataFileIntegrity VerifyDataFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return DataFileIntegrity.Failed;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < NovaraStore.HeaderSize) return DataFileIntegrity.Failed;
            var header = new byte[NovaraStore.HeaderSize];
            fs.ReadExactly(header);
            if (BitConverter.ToUInt32(header, 0) != NovaraStore.Magic) return DataFileIntegrity.Failed;
            var ver = header[4];
            var flag = header[5];


            if (ver != NovaraStore.FileVersionLegacy && ver != NovaraStore.FileVersionCurrent && ver != NovaraStore.FileVersionKdfHardened)
                return DataFileIntegrity.Failed;

            if (flag == NovaraStore.FlagEncrypted)
            {




                var isGcm = ver == NovaraStore.FileVersionCurrent || ver == NovaraStore.FileVersionKdfHardened;


                var minBody = isGcm ? 12 + 16 + 1 : 16;
                return fs.Length >= NovaraStore.HeaderSize + minBody
                    ? DataFileIntegrity.EncryptedStructured
                    : DataFileIntegrity.Failed;
            }
            if (flag != NovaraStore.FlagPlain) return DataFileIntegrity.Failed;
            if (ver != NovaraStore.FileVersionLegacy) return DataFileIntegrity.Failed;


            fs.Position = NovaraStore.HeaderSize;
            var stored = header.AsSpan(6, 16).ToArray();
            return MD5.HashData(fs).AsSpan().SequenceEqual(stored)
                ? DataFileIntegrity.DigestVerified
                : DataFileIntegrity.Failed;
        }
        catch
        {
            return DataFileIntegrity.Failed;
        }
    }
}
