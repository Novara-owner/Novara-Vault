using System.IO;
using System.Text;
using System.Text.Json;

namespace Novara.Services;








internal sealed class JsonlAuditStore<T> where T : class
{
    private readonly object _gate = new();
    private readonly string _prefix;
    private readonly long _rotateBytes;
    private readonly int _maxArchives;
    private string? _baseDirOverride;

    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal JsonlAuditStore(string prefix, long rotateBytes, int maxArchives)
    {
        _prefix = prefix;
        _rotateBytes = rotateBytes;
        _maxArchives = maxArchives;
    }


    internal void SetBaseDir(string? dir) => _baseDirOverride = dir;

    private string Dir => _baseDirOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CoreEnv.DataDirName, "logs");

    private string CurrentFile => Path.Combine(Dir, _prefix + ".log");


    internal void Append(T evt)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Dir);
                var current = new FileInfo(CurrentFile);
                var rotated = false;
                if (current.Exists && current.Length >= _rotateBytes)
                {
                    File.Move(CurrentFile, Path.Combine(Dir, $"{_prefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log"));
                    rotated = true;
                }



                var line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evt, Opts) + "\n");
                using (var fs = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    fs.Write(line, 0, line.Length);
                    fs.Flush();
                }


                if (rotated)
                {
                    var stale = Directory.GetFiles(Dir, _prefix + "-*.log")
                        .OrderByDescending(f => f, StringComparer.Ordinal)
                        .Skip(_maxArchives);
                    foreach (var f in stale) { try { File.Delete(f); } catch { } }
                }
            }
        }
        catch (Exception ex)
        {



            AuditWriteHealth.Report(_prefix + " append", ex);
        }
    }








    private readonly object _chainGate = new();
    private Task _chain = Task.CompletedTask;



    internal void AppendOrdered(T evt)
    {
        lock (_chainGate) _chain = _chain.ContinueWith(_ => Append(evt), TaskScheduler.Default);
    }






    internal bool ClearAllWithMarker(T marker)
    {
        Task<bool> work;
        lock (_chainGate)
        {
            work = _chain.ContinueWith(_ =>
            {
                if (!ClearAll()) return false;
                Append(marker);
                return true;
            }, TaskScheduler.Default);
            _chain = work;
        }
        try { work.Wait(); return work.Result; }
        catch { return false;  }
    }





    internal bool ClearAllOrdered()
    {
        Task<bool> work;
        lock (_chainGate)
        {
            work = _chain.ContinueWith(_ => ClearAll(), TaskScheduler.Default);
            _chain = work;
        }
        try { work.Wait(); return work.Result; }
        catch { return false;  }
    }


    internal void WaitForPendingWrites()
    {
        Task tail;
        lock (_chainGate) tail = _chain;
        try { tail.Wait(); } catch { }
    }


    internal List<T> ReadLatest(int max)
    {
        var result = new List<T>();
        if (max <= 0) return result;
        try
        {
            List<string> files;
            lock (_gate)
            {
                if (!Directory.Exists(Dir)) return result;
                files = new List<string>();
                if (File.Exists(CurrentFile)) files.Add(CurrentFile);
                files.AddRange(Directory.GetFiles(Dir, _prefix + "-*.log")
                    .OrderByDescending(f => f, StringComparer.Ordinal));
            }

            foreach (var file in files)
            {
                if (result.Count >= max) break;
                AppendTail(file, max, result);
            }
        }
        catch { }
        return result;
    }


    private static void AppendTail(string file, int pageCap, List<T> result)
    {
        try
        {
            var buffer = new List<string>(pageCap);
            foreach (var raw in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                buffer.Add(raw);
                if (buffer.Count > pageCap) buffer.RemoveAt(0);
            }
            for (var i = buffer.Count - 1; i >= 0 && result.Count < pageCap; i--)
            {
                try
                {
                    var evt = JsonSerializer.Deserialize<T>(buffer[i], Opts);
                    if (evt != null) result.Add(evt);
                }
                catch {  }
            }
        }
        catch { }
    }



    internal bool ClearAll()
    {
        var failed = 0;
        try
        {
            lock (_gate)
            {
                if (!Directory.Exists(Dir)) return true;
                foreach (var f in Directory.GetFiles(Dir, _prefix + "*.log"))
                {
                    try { File.Delete(f); }
                    catch { failed++; }
                }
            }
        }
        catch (Exception ex)
        {


            AuditWriteHealth.Report(_prefix + " clear", ex);
            return false;
        }
        if (failed > 0)
        {
            AuditWriteHealth.Report(_prefix + " clear",
                new IOException($"{failed} audit file(s) could not be deleted"));
            return false;
        }
        return true;
    }


    internal int CountAll()
    {
        try
        {
            lock (_gate)
            {
                if (!Directory.Exists(Dir)) return 0;
                var n = 0;
                if (File.Exists(CurrentFile)) n += CountLines(CurrentFile);
                foreach (var a in Directory.GetFiles(Dir, _prefix + "-*.log")) n += CountLines(a);
                return n;
            }
        }
        catch { return 0; }
    }

    private static int CountLines(string file)
    {
        try { return File.ReadLines(file).Count(l => l.Trim().Length > 0); }
        catch { return 0; }
    }


    internal static string ShortName(string fullPath)
    {
        try { return Path.GetFileName(fullPath.TrimEnd('\\', '/')); }
        catch { return fullPath; }
    }
}
