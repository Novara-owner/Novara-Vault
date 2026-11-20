using System.Text.Json;
using System.Text.Json.Serialization;

namespace Novara.Services;


public interface ISealClock
{

    long TickCount64 { get; }

    DateTimeOffset UtcNow { get; }
}


public sealed class SystemSealClock : ISealClock
{
    public static readonly SystemSealClock Instance = new();

    public long TickCount64 => Environment.TickCount64;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}



















public sealed class SealLockout
{
    public const int MaxFailures = 5;

    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly ISealClock _clock;

    public SealLockout(string path, ISealClock? clock = null)
    {
        _path = path;
        _clock = clock ?? SystemSealClock.Instance;
    }


    public int FailCount => Effective().FailCount;


    public TimeSpan Remaining
    {
        get
        {
            var state = Effective();
            var left = RemainingOf(state);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public bool IsLocked => Remaining > TimeSpan.Zero;





    public void RegisterFailure()
    {
        var state = Effective();
        if (RemainingOf(state) > TimeSpan.Zero) return;

        state.FailCount++;
        if (state.FailCount >= MaxFailures)
        {
            state.RemainingMs = (long)LockDuration.TotalMilliseconds;
            state.TickAtLock = _clock.TickCount64;
            state.WallAtLockUtc = _clock.UtcNow;
        }

        Save(state);
    }


    public void RegisterSuccess()
    {
        var state = Effective();
        if (state.FailCount == 0 && state.RemainingMs == 0) return;

        state.FailCount = 0;
        state.RemainingMs = 0;
        state.TickAtLock = 0;
        state.WallAtLockUtc = default;
        Save(state);
    }


    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {

        }
    }







    private State Effective()
    {
        var state = Load();

        if (state.RemainingMs <= 0) return state;
        if (RemainingOf(state) > TimeSpan.Zero) return state;

        state.RemainingMs = 0;
        state.TickAtLock = 0;
        state.FailCount = 0;
        state.WallAtLockUtc = default;
        Save(state);
        return state;
    }

    private TimeSpan RemainingOf(State state)
    {
        if (state.RemainingMs <= 0) return TimeSpan.Zero;

        var elapsed = ElapsedSinceLock(state);
        var left = state.RemainingMs - elapsed;
        return left > 0 ? TimeSpan.FromMilliseconds(left) : TimeSpan.Zero;
    }




    private long ElapsedSinceLock(State state)
    {
        var byTick = _clock.TickCount64 - state.TickAtLock;
        if (byTick >= 0) return byTick;

        var byWall = (_clock.UtcNow - state.WallAtLockUtc).TotalMilliseconds;
        return byWall > 0 ? (long)byWall : 0;
    }

    private State Load()
    {
        try
        {
            if (!File.Exists(_path)) return new State();

            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json)) return new State();

            return JsonSerializer.Deserialize<State>(json, JsonOptions) ?? new State();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {

            return new State();
        }
    }

    private void Save(State state)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
            var temporary = _path + ".tmp";

            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_path)) File.Replace(temporary, _path, destinationBackupFileName: null);
            else File.Move(temporary, _path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {

        }
    }


    private sealed class State
    {
        public bool Enabled { get; set; } = true;

        public int FailCount { get; set; }

        public long RemainingMs { get; set; }

        public long TickAtLock { get; set; }

        public DateTimeOffset WallAtLockUtc { get; set; }
    }
}
