using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Novara.Services;








public sealed class SealSpacePaths(string root)
{

    public string Root { get; } = root;

    public string KeyWrap => Path.Combine(Root, "keywrap.nskw");

    public string Index => Path.Combine(Root, "index.nseal");

    public string Journal => Path.Combine(Root, "journal.nsj");

    public string Lockout => Path.Combine(Root, "lockout-seal.dat");


    public string Hint => Path.Combine(Root, "hint.txt");

    public string Items => Path.Combine(Root, "items");

    public string ImportStaging => Path.Combine(Root, "import-staging");


    public string Staging => Path.Combine(Root, "staging");


    public static SealSpacePaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CoreEnv.DataDirName,
        "seal"));


    public string ItemPath(string itemId) => Path.Combine(Items, itemId + ".novaseal");


    public string SealingPath(string itemId) => Path.Combine(Items, itemId + ".sealing");





    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Items);
        Directory.CreateDirectory(ImportStaging);
        Directory.CreateDirectory(Staging);
    }






    public bool Contains(string candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;



        var full = Canonical(candidate);
        var prefix = Canonical(Root);
        if (!prefix.EndsWith(Path.DirectorySeparatorChar)) prefix += Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   full.TrimEnd(Path.DirectorySeparatorChar),
                   prefix.TrimEnd(Path.DirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }

    private const uint FileReadAttributes = 0x0080;
    private const uint ShareAll = 0x0007;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file, StringBuilder path, uint pathLength, uint flags);


    internal static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows()) return full;

        try
        {
            using var handle = CreateFileW(
                full, FileReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
            if (handle.IsInvalid) return full;

            var buffer = new StringBuilder(512);
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0) return full;
            if (length >= buffer.Capacity)
            {
                buffer = new StringBuilder((int)length + 1);
                if (GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0) == 0) return full;
            }

            var resolved = buffer.ToString();
            if (resolved.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + resolved[8..];
            if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) return resolved[4..];
            return resolved;
        }
        catch
        {
            return full;
        }
    }
}
