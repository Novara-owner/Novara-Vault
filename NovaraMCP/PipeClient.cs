using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace NovaraMCP;


public class McpForwardError : Exception
{
    public McpForwardError(string message) : base(message) { }
}

public sealed class McpPipeClient
{


    private static string PipeName => Novara.Ipc.McpIpcName.Current;
    private static string LegacyPipeName => Novara.Ipc.McpIpcName.LegacyName;

    private readonly string? _token;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private long _nextId;

    public McpPipeClient(string? token) => _token = token;




    private static string? ReadLineWithTimeout(System.IO.StreamReader reader, int seconds)
    {



        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try
        {
            return reader.ReadLineAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {

            throw new TimeoutException($"主进程响应超时（{seconds} 秒无应答）");
        }

    }

    public string Call(string method, JsonObject? args)
    {


        for (int attempt = 0; ; attempt++)
        {
            try
            {
                EnsureConnected();
                var req = new JsonObject
                {
                    ["id"] = ++_nextId,
                    ["method"] = method,

                    ["params"] = (args ?? new JsonObject()).DeepClone()
                };
                _writer!.WriteLine(req.ToJsonString());



                var line = ReadLineWithTimeout(_reader!, 60);
                if (line == null)
                {
                    if (attempt >= 1) throw new McpForwardError("主进程无响应");
                    ResetConnection();
                    continue;
                }
                var resp = JsonNode.Parse(line) as JsonObject ?? throw new McpForwardError("响应无效");
                if (resp["ok"]?.GetValue<bool>() == true)
                    return resp["result"]?.ToJsonString() ?? "null";
                var msg = resp["error"]?["message"]?.GetValue<string>() ?? "未知错误";
                throw new McpForwardError(msg);
            }
            catch (TimeoutException)
            {

                ResetConnection();
                throw new McpForwardError("主进程响应超时");
            }
            catch (IOException)
            {
                if (attempt >= 1) throw;
                ResetConnection();
            }
        }
    }

    private void ResetConnection()
    {
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
        _reader = null;
        _writer = null;
    }

    private void EnsureConnected()
    {
        if (_pipe is { IsConnected: true }) return;
        ConnectOnce();
    }

    private void ConnectOnce()
    {




        if (TryConnectTo(PipeName, 40)) return;
        if (TryConnectTo(LegacyPipeName, 1, launchIfMissing: false)) return;
        throw new McpForwardError("无法连接到 Novara 主进程，请确认 Novara 已安装并启动");
    }




    private bool TryConnectTo(string name, int attempts, bool launchIfMissing = true)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { pipe.Connect(500); }
            catch (TimeoutException) { pipe.Dispose(); if (attempt == 0 && launchIfMissing) LaunchNovara(); Thread.Sleep(250); continue; }
            catch { pipe.Dispose(); if (attempt == 0 && launchIfMissing) LaunchNovara(); Thread.Sleep(250); continue; }

            _pipe = pipe;
            _reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            _writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

            var hello = new JsonObject
            {
                ["type"] = "hello",
                ["token"] = _token ?? "",
                ["clientPath"] = GetParentProcessPath() ?? ""
            };
            _writer.WriteLine(hello.ToJsonString());
            var line = ReadLineWithTimeout(_reader, 130);
            if (line != null)
            {
                var resp = JsonNode.Parse(line) as JsonObject;
                if (resp != null && (resp["ok"]?.GetValue<bool>() == true)) return true;
                var err = resp?["error"]?.GetValue<string>() ?? "握手失败";


                ResetConnection();
                throw new McpForwardError(err);
            }
            ResetConnection();
            throw new McpForwardError("握手无响应：Novara 主进程未回应，请确认 Novara 已启动");
        }
        return false;
    }

    private void LaunchNovara()
    {
        var exe = FindNovaraExe();
        if (exe == null) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = exe, Arguments = "--mcp-background", UseShellExecute = true });
        }
        catch { }
    }

    private static string? FindNovaraExe()
    {

        var sideBySide = Path.Combine(AppContext.BaseDirectory, "Novara.exe");
        if (File.Exists(sideBySide)) return sideBySide;


        for (var dir = Path.GetFullPath(AppContext.BaseDirectory); ; )
        {
            if (File.Exists(Path.Combine(dir, "Novara.csproj")))
            {
                foreach (var cfg in new[] { "Debug", "Release" })
                    foreach (var platform in new[] { Path.Combine("bin", "x64"), "bin" })
                    {
                        var dev = Path.Combine(dir, platform, cfg, "net8.0-windows10.0.26100.0", "win-x64", "Novara.exe");
                        if (File.Exists(dev)) return dev;
                    }
            }
            var parent = Directory.GetParent(dir)?.FullName;
            if (string.IsNullOrEmpty(parent) || parent == dir) break;
            dir = parent;
        }
        return null;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    private static int GetParentPid(int pid)
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

    private static string? GetParentProcessPath()
    {
        int parent = GetParentPid(Environment.ProcessId);
        if (parent <= 0) return null;
        try
        {
            using var p = Process.GetProcessById(parent);
            return p.MainModule?.FileName;
        }
        catch { return null; }
    }
}
