using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Novara.Services;

internal static class McpClientIdentity
{



    internal readonly record struct AgentIdentity(string ImagePath, string FailureReason)
    {
        public static AgentIdentity Resolved(string imagePath) => new(imagePath, "");
        public static AgentIdentity Unresolved(string reason) => new("", reason);
    }




    public static AgentIdentity ResolveAgent(NamedPipeServerStream pipe)
    {
        try
        {
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientPid) || clientPid == 0)
                return AgentIdentity.Unresolved("无法确认管道对端进程");





            var clientImage = ImagePathOf((int)clientPid);
            if (clientImage is null)
                return AgentIdentity.Unresolved("无法确认管道客户端（NovaraMCP.exe）的映像路径");
            var clientProblem = VerifyClientImage(clientImage);
            if (clientProblem.Length > 0) return AgentIdentity.Unresolved(clientProblem);

            var parentPid = ParentPidOf((int)clientPid);




            if (parentPid <= 0) return AgentIdentity.Unresolved("无法确认启动 NovaraMCP 的 Agent 进程");







            var clientCreated = CreationTimeOf((int)clientPid);
            if (clientCreated is null) return AgentIdentity.Unresolved("无法确认 NovaraMCP 的启动时刻");
            var parentCreated = CreationTimeOf(parentPid);
            if (parentCreated is null)
                return AgentIdentity.Unresolved("启动 NovaraMCP 的 Agent 进程已经退出（它不能再作为授权身份）");
            if (parentCreated > clientCreated)
                return AgentIdentity.Unresolved("启动 NovaraMCP 的 Agent 进程已经退出，且该进程号已被另一个进程占用");

            var imagePath = ImagePathOf(parentPid);
            return imagePath is null
                ? AgentIdentity.Unresolved("无法确认启动 NovaraMCP 的进程映像路径")
                : AgentIdentity.Resolved(imagePath);
        }
        catch { return AgentIdentity.Unresolved("无法确认客户端进程身份"); }
    }





    private static string VerifyClientImage(string clientImagePath)
    {
        var name = Path.GetFileName(clientImagePath);
        if (!string.Equals(name, "NovaraMCP.exe", StringComparison.OrdinalIgnoreCase))
            return $"连接的进程不是 NovaraMCP.exe（{name}），已拒绝：只有随 Novara 发布的 MCP 转发器可以接入";

        var shipped = ShippedClientCandidates();
        if (shipped.Count == 0)
            return "无法定位随 Novara 发布的 NovaraMCP.exe，无法校验连接方身份（已拒绝）";

        var full = Path.GetFullPath(clientImagePath);
        foreach (var candidate in shipped)
            if (string.Equals(candidate, full, StringComparison.OrdinalIgnoreCase)) return "";

        var clientHash = HashFile(full);
        if (clientHash == null) return "无法读取连接方映像以校验身份（已拒绝）";
        foreach (var candidate in shipped)
            if (string.Equals(HashFile(candidate), clientHash, StringComparison.Ordinal)) return "";
        return "连接的 NovaraMCP.exe 与随 Novara 发布的版本不一致（已拒绝），请使用主程序目录中的 NovaraMCP.exe";
    }




    private static List<string> ShippedClientCandidates()
    {
        var found = new List<string>();
        void Probe(string dir)
        {
            if (!Directory.Exists(dir)) return;
            try
            {
                foreach (var exe in new[]
                         {
                             Path.Combine(dir, "NovaraMCP.exe"),
                             Path.Combine(dir, "win-x64", "NovaraMCP.exe"),
                         })
                    if (File.Exists(exe)) found.Add(Path.GetFullPath(exe));
                foreach (var tfm in Directory.EnumerateDirectories(dir, "net*"))
                {
                    var exe = Path.Combine(tfm, "win-x64", "NovaraMCP.exe");
                    if (File.Exists(exe)) found.Add(Path.GetFullPath(exe));
                }
            }
            catch { }
        }

        try
        {
            Probe(AppContext.BaseDirectory);
            for (var dir = Path.GetFullPath(AppContext.BaseDirectory); ; )
            {
                var project = Path.Combine(dir, "NovaraMCP");
                if (File.Exists(Path.Combine(project, "NovaraMCP.csproj")))
                    foreach (var cfg in new[] { "Debug", "Release" })
                        foreach (var platform in new[] { Path.Combine("bin", "x64", cfg), Path.Combine("bin", cfg) })
                            Probe(Path.Combine(project, platform));
                var parent = Directory.GetParent(dir)?.FullName;
                if (string.IsNullOrEmpty(parent) || parent == dir) break;
                dir = parent;
            }
        }
        catch { }
        return found;
    }

    private static string? HashFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch { return null; }
    }





    private static ulong? CreationTimeOf(int pid)
    {
        if (pid <= 0) return null;
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            if (!GetProcessTimes(handle, out var created, out _, out _, out _)) return null;
            return ((ulong)created.dwHighDateTime << 32) | created.dwLowDateTime;
        }
        finally { CloseHandle(handle); }
    }





    public static string? ResolveProcessImagePath(int pid) => ImagePathOf(pid);




    private static string? ImagePathOf(int pid)
    {
        if (pid <= 0) return null;
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            uint size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageNameW(handle, 0, buffer, ref size)) return null;
            return buffer.ToString();
        }
        finally { CloseHandle(handle); }
    }



    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;


    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr hProcess, out FILETIME lpCreationTime,
        out FILETIME lpExitTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private static int ParentPidOf(int pid)
    {
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero) return 0;
        try
        {
            var pe = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (Process32FirstW(snapshot, ref pe))
            {
                do
                {
                    if (pe.th32ProcessID == (uint)pid) return (int)pe.th32ParentProcessID;
                } while (Process32NextW(snapshot, ref pe));
            }
        }
        finally { CloseHandle(snapshot); }
        return 0;
    }
}
