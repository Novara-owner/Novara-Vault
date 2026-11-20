using System.Security.Cryptography;
using System.Text;

namespace Novara.Services;


public enum SealStoreOutcome
{
    Ok = 0,


    AlreadyInitialized,


    NotInitialized,


    WrongPassword,


    VersionUnsupported,


    WeakPassword,


    SameAsMainPassword,


    Corrupt,


    IoError,
}



















public sealed class SealSpaceStore
{

    public const int MaxHintBytes = 256;

    public SealSpaceStore(SealSpacePaths paths)
        : this(paths, SealKdfParams.Default)
    {
    }





    internal SealSpaceStore(SealSpacePaths paths, SealKdfParams kdfParameters)
    {
        Paths = paths;
        KdfParameters = kdfParameters;
    }

    public SealSpacePaths Paths { get; }

    internal SealKdfParams KdfParameters { get; }


    public bool Exists() => File.Exists(Paths.KeyWrap);


    public SealStoreOutcome Initialize(string password, string? hint, out byte[] vmk)
    {
        vmk = Array.Empty<byte>();

        if (Exists()) return SealStoreOutcome.AlreadyInitialized;

        var (acceptable, _) = SealPasswordPolicy.Evaluate(password);
        if (!acceptable) return SealStoreOutcome.WeakPassword;

        if (PasswordService.Exists() && PasswordService.Verify(password))
            return SealStoreOutcome.SameAsMainPassword;

        var newVmk = RandomNumberGenerator.GetBytes(SealFormat.KeySize);
        var transferred = false;
        try
        {
            Paths.EnsureCreated();

            var wrap = SealKeyWrap.Wrap(newVmk, password, KdfParameters);
            WriteAtomically(Paths.KeyWrap, wrap);


            SealIndex.Empty.Save(Paths.Index, newVmk);
            WriteHintCore(hint);

            vmk = newVmk;
            transferred = true;
            return SealStoreOutcome.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {


            TryDelete(Paths.KeyWrap);
            TryDelete(Paths.Index);
            return SealStoreOutcome.IoError;
        }
        finally
        {
            if (!transferred) CryptographicOperations.ZeroMemory(newVmk);
        }
    }




    public SealStoreOutcome TryUnlock(string password, out byte[] vmk, out bool weakened)
    {
        vmk = Array.Empty<byte>();
        weakened = false;

        if (!Exists()) return SealStoreOutcome.NotInitialized;

        byte[] wrap;
        try
        {
            wrap = File.ReadAllBytes(Paths.KeyWrap);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SealStoreOutcome.IoError;
        }


        if (!SealKeyWrap.LooksLikeKeyWrap(wrap)) return SealStoreOutcome.Corrupt;

        if (!SealKeyWrap.TryUnwrap(wrap, password, out var opened, out var wrapOutcome))
        {
            return wrapOutcome == SealKeyWrapOutcome.VersionUnsupported
                ? SealStoreOutcome.VersionUnsupported
                : SealStoreOutcome.WrongPassword;
        }



        weakened = wrapOutcome == SealKeyWrapOutcome.OkWeakened;

        vmk = opened;
        return SealStoreOutcome.Ok;
    }






    public bool MatchesPassword(string password)
    {
        if (TryUnlock(password, out var vmk, out _) != SealStoreOutcome.Ok) return false;

        CryptographicOperations.ZeroMemory(vmk);
        return true;
    }





    public SealStoreOutcome ChangePassword(string oldPassword, string newPassword)
    {
        if (!Exists()) return SealStoreOutcome.NotInitialized;


        if (!MatchesPassword(oldPassword)) return SealStoreOutcome.WrongPassword;

        var (acceptable, _) = SealPasswordPolicy.Evaluate(newPassword);
        if (!acceptable) return SealStoreOutcome.WeakPassword;

        if (PasswordService.Exists() && PasswordService.Verify(newPassword))
            return SealStoreOutcome.SameAsMainPassword;

        if (TryUnlock(oldPassword, out var vmk, out _) != SealStoreOutcome.Ok)
            return SealStoreOutcome.WrongPassword;

        try
        {
            var wrap = SealKeyWrap.Wrap(vmk, newPassword, KdfParameters);
            WriteAtomically(Paths.KeyWrap, wrap);
            return SealStoreOutcome.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SealStoreOutcome.IoError;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }





    public string? ReadHint()
    {
        try
        {
            if (!File.Exists(Paths.Hint)) return null;

            var bytes = File.ReadAllBytes(Paths.Hint);
            if (bytes.Length == 0) return null;

            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }


    public SealStoreOutcome WriteHint(string? hint)
    {
        try
        {
            WriteHintCore(hint);
            return SealStoreOutcome.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SealStoreOutcome.IoError;
        }
    }





    public long MeasureItemsBytes()
    {
        try
        {
            if (!Directory.Exists(Paths.Items)) return 0;
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(Paths.Items, "*", SearchOption.TopDirectoryOnly))
            {
                try { total += new FileInfo(file).Length; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }










    public SealStoreOutcome DeleteAll()
    {
        try
        {
            if (!Directory.Exists(Paths.Root)) return SealStoreOutcome.Ok;
            var tombstone = Paths.Root.TrimEnd(Path.DirectorySeparatorChar) + ".deleting";
            TryDeleteDirectory(tombstone);
            Directory.Move(Paths.Root, tombstone);


            TryDeleteDirectory(tombstone);
            return SealStoreOutcome.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SealStoreOutcome.IoError;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {

        }
    }



    private void WriteHintCore(string? hint)
    {
        if (string.IsNullOrEmpty(hint))
        {
            if (File.Exists(Paths.Hint)) File.Delete(Paths.Hint);
            return;
        }

        WriteAtomically(Paths.Hint, TruncateUtf8(hint, MaxHintBytes));
    }


    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var temporary = path + ".tmp";

        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
        else File.Move(temporary, path);
    }





    internal static byte[] TruncateUtf8(string text, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maxBytes) return bytes;

        var cut = maxBytes;
        while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) cut--;

        return bytes[..cut];
    }
}
