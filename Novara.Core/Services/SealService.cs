using System.Collections.Concurrent;
using System.Security.Cryptography;
using Novara.Models;

namespace Novara.Services;


public enum SealServiceStatus
{
    Ok = 0,


    NotUnlocked,


    RequiresFullSession,

    AlreadyInitialized,
    NotInitialized,
    WrongPassword,
    WeakPassword,
    SameAsMainPassword,
    VersionUnsupported,
    Corrupt,

    IndexFull,
    IoError,


    Busy,


    InvalidRequest,
}


public readonly record struct SealCallResult<T>(SealServiceStatus Status, T? Value)
{
    public bool Ok => Status == SealServiceStatus.Ok;

    public static SealCallResult<T> Failed(SealServiceStatus status) => new(status, default);
}


















public readonly record struct SealUsage(int Total, int Space, int Capsules, long UsedBytes);




public readonly record struct SealRecoveryRun(
    int Applied,
    IReadOnlyList<SealRecoveryAction> Deferred,
    int SweptContainers,
    int SweptArchives,
    int SweptBundles,
    SealJournalStatus JournalStatus,
    bool HasMalformedRecords);

public sealed class SealService : IDisposable
{
    private readonly SealSpacePaths _paths;
    private readonly SealSpaceStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _entryGates = new(StringComparer.Ordinal);
    private readonly object _gate = new();




    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private SealSession? _session;
    private long _sessionEpoch;
    private bool _disposed;
    private bool _lastUnlockWeakened;

    public SealService(SealSpacePaths paths, Func<DateTimeOffset>? clock = null)
        : this(paths, new SealSpaceStore(paths), clock)
    {
    }


    internal SealService(SealSpacePaths paths, SealSpaceStore store, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(store);

        _paths = paths;
        _store = store;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }


    public static SealService Current { get; } = new(SealSpacePaths.Default);


    public bool IsUnlocked => _session is { IsUnlocked: true };



    internal SemaphoreSlim WriteGate => _writeGate;


    public SealSessionMode Mode => _session?.Mode ?? SealSessionMode.Normal;

    public SealServiceStatus Status()
        => IsUnlocked ? SealServiceStatus.Ok : SealServiceStatus.NotUnlocked;



    public Task<SealServiceStatus> InitializeAsync(
        string password, string? hint = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var outcome = _store.Initialize(password, hint, out var vmk);
        CryptographicOperations.ZeroMemory(vmk);

        return Task.FromResult(Map(outcome));
    }

    public async Task<SealServiceStatus> UnlockAsync(
        string password, SealSessionMode mode = SealSessionMode.Normal, CancellationToken cancellationToken = default)
        => (await UnlockWithEpochAsync(password, mode, cancellationToken).ConfigureAwait(false)).Status;



    public Task<(SealServiceStatus Status, long Epoch)> UnlockWithEpochAsync(
        string password, SealSessionMode mode = SealSessionMode.Normal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();



        long attempt;
        lock (_gate) attempt = ++_sessionEpoch;

        var outcome = _store.TryUnlock(password, out var vmk, out var weakened);
        if (outcome != SealStoreOutcome.Ok) return Task.FromResult((Map(outcome), attempt));

        lock (_gate)
        {
            if (attempt != _sessionEpoch)
            {
                CryptographicOperations.ZeroMemory(vmk);
                return Task.FromResult((SealServiceStatus.NotUnlocked, attempt));
            }
            _session?.Dispose();
            _session = SealSession.Open(vmk, mode);
            _lastUnlockWeakened = weakened;
        }

        CryptographicOperations.ZeroMemory(vmk);
        return Task.FromResult((SealServiceStatus.Ok, attempt));
    }





    public bool ConsumeUnlockWeakened()
    {
        lock (_gate)
        {
            var weakened = _lastUnlockWeakened;
            _lastUnlockWeakened = false;
            return weakened;
        }
    }


    public void Lock()
    {
        lock (_gate)
        {
            _sessionEpoch++;
            _session?.Dispose();
            _session = null;
            _lastUnlockWeakened = false;
        }
    }


    public long SessionEpoch { get { lock (_gate) return _sessionEpoch; } }




    public bool TryLockIfEpoch(long epoch)
    {
        lock (_gate)
        {
            if (_sessionEpoch != epoch) return false;
            _sessionEpoch++;
            _session?.Dispose();
            _session = null;
            _lastUnlockWeakened = false;
            return true;
        }
    }



    public SealCallResult<IReadOnlyList<SealedItem>> ListItems()
    {
        var vmk = TryVmkCopy();
        if (vmk is null) return SealCallResult<IReadOnlyList<SealedItem>>.Failed(SealServiceStatus.NotUnlocked);

        try
        {
            return new SealCallResult<IReadOnlyList<SealedItem>>(
                SealServiceStatus.Ok, SealIndex.LoadInitialized(_paths.Index, vmk).Items);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return SealCallResult<IReadOnlyList<SealedItem>>.Failed(StatusOf(ex));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }



    public SealCallResult<long> GetObservedMaxUtc()
    {
        var vmk = TryVmkCopy();
        if (vmk is null) return SealCallResult<long>.Failed(SealServiceStatus.NotUnlocked);

        try
        {
            return new SealCallResult<long>(
                SealServiceStatus.Ok, SealIndex.LoadInitialized(_paths.Index, vmk).ObservedMaxUtc);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return SealCallResult<long>.Failed(StatusOf(ex));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }








    public SealCallResult<long> ResetClockBaseline()
    {
        if (!IsUnlocked) return SealCallResult<long>.Failed(SealServiceStatus.NotUnlocked);
        if (Mode == SealSessionMode.Direct)
            return SealCallResult<long>.Failed(SealServiceStatus.RequiresFullSession);

        var vmk = TryVmkCopy();
        if (vmk is null) return SealCallResult<long>.Failed(SealServiceStatus.NotUnlocked);

        if (!_writeGate.Wait(0))
        {
            CryptographicOperations.ZeroMemory(vmk);
            return SealCallResult<long>.Failed(SealServiceStatus.Busy);
        }

        try
        {
            var index = SealIndex.LoadInitialized(_paths.Index, vmk);
            var baseline = _clock().ToUnixTimeSeconds();
            index.WithObservedMaxReset(baseline).Save(_paths.Index, vmk);
            return new SealCallResult<long>(SealServiceStatus.Ok, baseline);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return SealCallResult<long>.Failed(StatusOf(ex));
        }
        finally
        {
            _writeGate.Release();
            CryptographicOperations.ZeroMemory(vmk);
        }
    }





    public SealCallResult<bool> CanTake(string itemId)
    {
        var vmk = TryVmkCopy();
        if (vmk is null) return SealCallResult<bool>.Failed(SealServiceStatus.NotUnlocked);

        try
        {
            var index = SealIndex.LoadInitialized(_paths.Index, vmk);
            var item = index.Find(itemId);
            if (item is null) return new SealCallResult<bool>(SealServiceStatus.Ok, false);

            var verdict = SealTimeLock.Evaluate(item, _clock(), index.ObservedMaxUtc);
            return new SealCallResult<bool>(
                SealServiceStatus.Ok, verdict is SealTakeVerdict.TakeAllowed or SealTakeVerdict.Expired);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return SealCallResult<bool>.Failed(StatusOf(ex));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }



    public async Task<SealCallResult<IReadOnlyList<SealResult>>> SealAsync(
        IReadOnlyList<SealRequest> requests,
        IProgress<SealProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var vmk = TryVmkCopy();
        if (vmk is null)
            return SealCallResult<IReadOnlyList<SealResult>>.Failed(SealServiceStatus.NotUnlocked);


        if (!_writeGate.Wait(0))
        {
            CryptographicOperations.ZeroMemory(vmk);
            return SealCallResult<IReadOnlyList<SealResult>>.Failed(SealServiceStatus.Busy);
        }

        try
        {
            var operation = new SealSealOperation(_paths, vmk);
            return new SealCallResult<IReadOnlyList<SealResult>>(
                SealServiceStatus.Ok, await operation.RunAsync(requests, progress, cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (InvalidOperationException)
        {

            return SealCallResult<IReadOnlyList<SealResult>>.Failed(SealServiceStatus.Corrupt);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return SealCallResult<IReadOnlyList<SealResult>>.Failed(StatusOf(ex));
        }
        finally
        {
            _writeGate.Release();
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    public async Task<SealCallResult<SealTakeResult>> TakeAsync(
        SealTakeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vmk = TryVmkCopy();
        if (vmk is null)
            return SealCallResult<SealTakeResult>.Failed(SealServiceStatus.NotUnlocked);


        if (!_writeGate.Wait(0))
        {
            CryptographicOperations.ZeroMemory(vmk);
            return new SealCallResult<SealTakeResult>(
                SealServiceStatus.Busy, new SealTakeResult(request.ItemId, SealTakeStatus.Busy));
        }

        try
        {
            var gate = _entryGates.GetOrAdd(request.ItemId, _ => new SemaphoreSlim(1, 1));

            if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return new SealCallResult<SealTakeResult>(
                    SealServiceStatus.Busy, new SealTakeResult(request.ItemId, SealTakeStatus.Busy));
            }

            try
            {
                var operation = new SealTakeOperation(_paths, vmk, clock: _clock);

                return new SealCallResult<SealTakeResult>(
                    SealServiceStatus.Ok, await operation.RunAsync(request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (IsLoadFailure(ex))
            {
                return SealCallResult<SealTakeResult>.Failed(StatusOf(ex));
            }
            catch (InvalidOperationException)
            {


                return SealCallResult<SealTakeResult>.Failed(SealServiceStatus.Corrupt);
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            _writeGate.Release();
            CryptographicOperations.ZeroMemory(vmk);
        }
    }





    public Task<SealServiceStatus> DestroyAsync(string itemId, CancellationToken cancellationToken = default)
    {
        var vmk = TryVmkCopy();
        if (vmk is null) return Task.FromResult(SealServiceStatus.NotUnlocked);
        if (!_writeGate.Wait(0))
        {
            CryptographicOperations.ZeroMemory(vmk);
            return Task.FromResult(SealServiceStatus.Busy);
        }

        try
        {
            var index = SealIndex.LoadInitialized(_paths.Index, vmk);
            if (index.Find(itemId) is null) return Task.FromResult(SealServiceStatus.Ok);

            index.WithoutItem(itemId).Save(_paths.Index, vmk);

            var container = _paths.ItemPath(itemId);
            if (File.Exists(container)) File.Delete(container);

            return Task.FromResult(SealServiceStatus.Ok);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return Task.FromResult(StatusOf(ex));
        }
        finally
        {
            _writeGate.Release();
            CryptographicOperations.ZeroMemory(vmk);
        }
    }







    public SealCallResult<string?> ReadHint()
    {
        try
        {
            return new SealCallResult<string?>(SealServiceStatus.Ok, _store.ReadHint());
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return SealCallResult<string?>.Failed(StatusOf(ex));
        }
    }



    public SealCallResult<SealUsage> GetUsage()
    {
        var vmk = TryVmkCopy();
        if (vmk is null) return SealCallResult<SealUsage>.Failed(SealServiceStatus.NotUnlocked);

        try
        {
            var items = SealIndex.LoadInitialized(_paths.Index, vmk).Items;
            var capsules = items.Count(i => i.IsCapsule);

            var used = _store.MeasureItemsBytes();

            return new SealCallResult<SealUsage>(
                SealServiceStatus.Ok, new SealUsage(items.Count, items.Count - capsules, capsules, used));
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return SealCallResult<SealUsage>.Failed(StatusOf(ex));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }



    public async Task<SealServiceStatus> WriteHintAsync(string? hint)
    {
        if (!IsUnlocked) return SealServiceStatus.NotUnlocked;
        if (Mode == SealSessionMode.Direct) return SealServiceStatus.RequiresFullSession;


        if (!_writeGate.Wait(0)) return SealServiceStatus.Busy;
        try { return await Task.Run(() => Map(_store.WriteHint(hint))).ConfigureAwait(false); }
        finally { _writeGate.Release(); }
    }





    public async Task<SealCallResult<SealRecoveryRun>> RunAutomaticRecoveryAsync(CancellationToken cancellationToken = default)
    {
        var vmk = TryVmkCopy();
        if (vmk is null) return SealCallResult<SealRecoveryRun>.Failed(SealServiceStatus.NotUnlocked);






        return await Task.Run(async () =>
        {
            try
            {
                await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                CryptographicOperations.ZeroMemory(vmk);
                throw;
            }
            try
            {
                try
                {
                    var index = SealIndex.LoadInitialized(_paths.Index, vmk);
                    var journal = SealJournal.LoadRequiredIfPresent(_paths.Journal, vmk);
                    var plan = SealRecovery.Plan(journal, index, new SealFileProbe(_paths, index));

                    var applied = plan.Actions.Any(a => a.IsAutomatic)
                        ? await SealRecovery.ApplyAsync(index, journal, plan, _paths, vmk, cancellationToken).ConfigureAwait(false)
                        : new SealRecoveryResult(0, plan.Actions.Where(a => a.NeedsUserDecision).ToList());


                    var after = SealIndex.LoadInitialized(_paths.Index, vmk);
                    var afterJournal = SealJournal.LoadRequiredIfPresent(_paths.Journal, vmk);
                    var swept = SealRecovery.Sweep(_paths, after, afterJournal);



                    var autoDeferred = applied.Deferred.Where(a => a.IsAutomatic).ToList();

                    return new SealCallResult<SealRecoveryRun>(SealServiceStatus.Ok, new SealRecoveryRun(
                        applied.AppliedCount, autoDeferred,
                        swept.ContainersRemoved, swept.StagingArchivesRemoved, swept.ImportBundlesRemoved,
                        plan.JournalStatus, plan.HasMalformedRecords));
                }
                catch (Exception ex) when (IsLoadFailure(ex))
                {
                    return SealCallResult<SealRecoveryRun>.Failed(StatusOf(ex));
                }
            }
            finally
            {
                _writeGate.Release();
                CryptographicOperations.ZeroMemory(vmk);
            }
        }).ConfigureAwait(false);
    }







    public SealCallResult<SealRecoveryPlan> GetRecoveryPlan()
    {
        var vmk = TryVmkCopy();
        if (vmk is null) return SealCallResult<SealRecoveryPlan>.Failed(SealServiceStatus.NotUnlocked);

        try
        {
            var index = SealIndex.LoadInitialized(_paths.Index, vmk);
            var journal = SealJournal.LoadRequiredIfPresent(_paths.Journal, vmk);
            var plan = SealRecovery.Plan(journal, index, new SealFileProbe(_paths, index));
            return new SealCallResult<SealRecoveryPlan>(SealServiceStatus.Ok, plan);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return SealCallResult<SealRecoveryPlan>.Failed(StatusOf(ex));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }





    public SealCallResult<IReadOnlyList<SealRecoveryEntry>> GetRecoveryEntries()
    {
        var vmk = TryVmkCopy();
        if (vmk is null) return SealCallResult<IReadOnlyList<SealRecoveryEntry>>.Failed(SealServiceStatus.NotUnlocked);

        try
        {
            var index = SealIndex.LoadInitialized(_paths.Index, vmk);
            var journal = SealJournal.LoadRequiredIfPresent(_paths.Journal, vmk);
            var plan = SealRecovery.Plan(journal, index, new SealFileProbe(_paths, index));

            var entries = new List<SealRecoveryEntry>();
            foreach (var action in plan.Actions)
            {
                if (!action.NeedsUserDecision) continue;

                var item = index.Find(action.ItemId);
                var record = journal.Records.LastOrDefault(
                    r => string.Equals(r.OpId, action.OpId, StringComparison.Ordinal));


                var source = action.Kind switch
                {
                    SealRecoveryActionKind.OrphanSourceRemains => item?.SourcePath ?? record?.SourcePath,
                    SealRecoveryActionKind.OfferRetrySeal => record?.SourcePath,
                    SealRecoveryActionKind.StagingOnly => record?.SourcePath,
                    SealRecoveryActionKind.SourceLost => record?.SourcePath,
                    _ => null,
                };
                var name = item?.OriginalName
                    ?? (string.IsNullOrEmpty(source) ? "" : Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)));




                var targetPath = record?.TargetPath;
                if (action.Kind == SealRecoveryActionKind.AskCleanupPartialTarget && !string.IsNullOrEmpty(targetPath))
                {
                    var scratch = SealTakeOperation.TemporaryArchiveFor(targetPath!, action.ItemId);
                    if (File.Exists(scratch)) targetPath = scratch;
                }

                entries.Add(new SealRecoveryEntry(action, source, targetPath, name ?? ""));
            }

            return new SealCallResult<IReadOnlyList<SealRecoveryEntry>>(SealServiceStatus.Ok, entries);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return SealCallResult<IReadOnlyList<SealRecoveryEntry>>.Failed(StatusOf(ex));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }











    public SealCallResult<bool> DiscardUnreadableJournal()
    {
        var vmk = TryVmkCopy();
        if (vmk is null) return SealCallResult<bool>.Failed(SealServiceStatus.NotUnlocked);

        if (!_writeGate.Wait(0))
        {
            CryptographicOperations.ZeroMemory(vmk);
            return SealCallResult<bool>.Failed(SealServiceStatus.Busy);
        }

        try
        {



            if (!File.Exists(_paths.Journal))
            {
                try
                {
                    using var probe = new FileStream(_paths.Journal, FileMode.Open, FileAccess.Read,
                                                     FileShare.ReadWrite | FileShare.Delete);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    return new SealCallResult<bool>(SealServiceStatus.Ok, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return SealCallResult<bool>.Failed(SealServiceStatus.IoError);
                }
            }


            var journal = SealJournal.LoadRequiredIfPresent(_paths.Journal, vmk);
            if (journal.Status == SealJournalStatus.Ok)
                return SealCallResult<bool>.Failed(SealServiceStatus.InvalidRequest);

            if (journal.Reason == SealCorruptReason.IoUnavailable)
                return SealCallResult<bool>.Failed(SealServiceStatus.IoError);

            File.Delete(_paths.Journal);
            return new SealCallResult<bool>(SealServiceStatus.Ok, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SealCallResult<bool>.Failed(SealServiceStatus.IoError);
        }
        finally
        {
            _writeGate.Release();
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    public SealCallResult<bool> DiscardMalformedJournalRecords()
    {
        var vmk = TryVmkCopy();
        if (vmk is null) return SealCallResult<bool>.Failed(SealServiceStatus.NotUnlocked);

        if (!_writeGate.Wait(0))
        {
            CryptographicOperations.ZeroMemory(vmk);
            return SealCallResult<bool>.Failed(SealServiceStatus.Busy);
        }

        try
        {
            if (!File.Exists(_paths.Journal))
                return new SealCallResult<bool>(SealServiceStatus.Ok, true);

            var journal = SealJournal.LoadRequiredIfPresent(_paths.Journal, vmk);
            if (journal.Status != SealJournalStatus.Ok)
                return SealCallResult<bool>.Failed(SealServiceStatus.Corrupt);
            if (!journal.HasMalformedRecords)
                return new SealCallResult<bool>(SealServiceStatus.Ok, true);

            journal.RemoveMalformedRecords().Save(_paths.Journal, vmk);
            return new SealCallResult<bool>(SealServiceStatus.Ok, true);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return SealCallResult<bool>.Failed(StatusOf(ex));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SealCallResult<bool>.Failed(SealServiceStatus.IoError);
        }
        catch (InvalidOperationException)
        {
            return SealCallResult<bool>.Failed(SealServiceStatus.Corrupt);
        }
        finally
        {
            _writeGate.Release();
            CryptographicOperations.ZeroMemory(vmk);
        }
    }





    public async Task<SealServiceStatus> ApplyRecoveryAsync(
        SealRecoveryAction action, SealRecoveryChoice choice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);





        if (!SealRecovery.ChoiceMatches(action.Kind, choice)) return SealServiceStatus.InvalidRequest;

        var vmk = TryVmkCopy();
        if (vmk is null) return SealServiceStatus.NotUnlocked;
        if (!_writeGate.Wait(0))
        {
            CryptographicOperations.ZeroMemory(vmk);
            return SealServiceStatus.Busy;
        }

        try
        {


            var handled = await Task.Run(async () =>
            {
                var index = SealIndex.LoadInitialized(_paths.Index, vmk);
                var journal = SealJournal.LoadRequiredIfPresent(_paths.Journal, vmk);
                return await SealRecovery.ResolveAsync(
                    index, journal, _paths, vmk, action, choice, new SealFileProbe(_paths, index), cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            return handled ? SealServiceStatus.Ok : SealServiceStatus.IoError;
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return StatusOf(ex);
        }
        finally
        {
            _writeGate.Release();
            CryptographicOperations.ZeroMemory(vmk);
        }
    }



    public Task<SealServiceStatus> ChangePasswordAsync(
        string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (!IsUnlocked) return Task.FromResult(SealServiceStatus.NotUnlocked);
        if (Mode == SealSessionMode.Direct) return Task.FromResult(SealServiceStatus.RequiresFullSession);

        cancellationToken.ThrowIfCancellationRequested();





        if (!_writeGate.Wait(0)) return Task.FromResult(SealServiceStatus.Busy);
        try
        {
            return Task.FromResult(Map(_store.ChangePassword(currentPassword, newPassword)));
        }
        finally
        {
            _writeGate.Release();
        }
    }






    public async Task<SealServiceStatus> ExportAsync(
        string targetPath,
        string currentPassword,
        byte kind = SealBundleWriter.KindItems,
        byte[]? backupBlock = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsUnlocked) return SealServiceStatus.NotUnlocked;
        if (Mode == SealSessionMode.Direct) return SealServiceStatus.RequiresFullSession;

        var vmk = TryVmkCopy();
        if (vmk is null) return SealServiceStatus.NotUnlocked;

        try
        {
            if (!_store.MatchesPassword(currentPassword)) return SealServiceStatus.WrongPassword;




            if (!_writeGate.Wait(0)) return SealServiceStatus.Busy;

            try
            {



                if (HasPendingRecovery(vmk)) return SealServiceStatus.Corrupt;

                var index = SealIndex.LoadInitialized(_paths.Index, vmk);
                var items = index.Items
                    .Select(item => new SealBundleItem(item.Id, _paths.ItemPath(item.Id)))
                    .ToList();

                await SealBundleWriter.WriteAsync(
                        targetPath,
                        new SealBundleOptions(
                            vmk, index, items, currentPassword, kind,
                            _clock().ToUnixTimeSeconds(), AppVersionCode(), backupBlock),
                        cancellationToken)
                    .ConfigureAwait(false);

                return SealServiceStatus.Ok;
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return StatusOf(ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }






    public async Task<SealCallResult<SealImportResult>> ImportAsync(
        string bundlePath,
        string bundlePassword,
        string? newLocalPassword = null,
        CancellationToken cancellationToken = default)
    {
        if (newLocalPassword is not null && !_store.Exists())
        {


            long importEpoch;
            lock (_gate) importEpoch = _sessionEpoch;

            var created = _store.Initialize(newLocalPassword, null, out var initialized);
            if (created != SealStoreOutcome.Ok)
                return SealCallResult<SealImportResult>.Failed(Map(created));

            try
            {
                var fresh = await SealImport.RunAsync(bundlePath, bundlePassword, _paths, initialized, cancellationToken)
                    .ConfigureAwait(false);





                if (fresh.Status != SealImportStatus.Ok)
                {
                    try
                    {
                        SealIntegration.ClearPrivateSpace(_paths);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {

                    }
                }

                if (fresh.Status == SealImportStatus.Ok)
                {




                    lock (_gate)
                    {
                        if (_sessionEpoch == importEpoch)
                        {
                            _session?.Dispose();
                            _session = SealSession.Open(initialized, SealSessionMode.Normal);
                        }
                    }
                }

                return new SealCallResult<SealImportResult>(MapImport(fresh.Status), fresh);
            }
            catch (OperationCanceledException)
            {



                try { SealIntegration.ClearPrivateSpace(_paths); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(initialized);
            }
        }

        if (!IsUnlocked) return SealCallResult<SealImportResult>.Failed(SealServiceStatus.NotUnlocked);
        if (Mode == SealSessionMode.Direct)
            return SealCallResult<SealImportResult>.Failed(SealServiceStatus.RequiresFullSession);

        var vmk = TryVmkCopy();
        if (vmk is null) return SealCallResult<SealImportResult>.Failed(SealServiceStatus.NotUnlocked);


        if (!_writeGate.Wait(0))
        {
            CryptographicOperations.ZeroMemory(vmk);
            return SealCallResult<SealImportResult>.Failed(SealServiceStatus.Busy);
        }

        try
        {

            if (HasPendingRecovery(vmk))
                return SealCallResult<SealImportResult>.Failed(SealServiceStatus.Corrupt);

            var result = await SealImport.RunAsync(bundlePath, bundlePassword, _paths, vmk, cancellationToken)
                .ConfigureAwait(false);

            return new SealCallResult<SealImportResult>(MapImport(result.Status), result);
        }
        finally
        {
            _writeGate.Release();
            CryptographicOperations.ZeroMemory(vmk);
        }
    }







    public static SealBundlePeekResult PeekBundle(string bundlePath) => SealBundleReader.Peek(bundlePath);



    public bool IsInitialized() => _store.Exists();




    public bool MatchesSpacePassword(string password)
        => IsInitialized() && new SealSpaceStore(_paths).MatchesPassword(password);








    private bool HasPendingRecovery(byte[] vmk)
    {
        try
        {
            var journal = SealJournal.LoadRequiredIfPresent(_paths.Journal, vmk);
            if (journal.Status != SealJournalStatus.Ok || journal.HasMalformedRecords) return true;

            var index = SealIndex.LoadInitialized(_paths.Index, vmk);
            var plan = SealRecovery.Plan(journal, index, new SealFileProbe(_paths, index));
            return plan.HasPendingDecisions;
        }
        catch (SealCorruptException)
        {
            return true;
        }



    }






    public async Task<SealServiceStatus> ExtractBundleBackupAsync(
        string bundlePath,
        string bundlePassword,
        string destination,
        CancellationToken cancellationToken = default)
    {
        if (!IsUnlocked) return SealServiceStatus.NotUnlocked;


        if (Mode == SealSessionMode.Direct) return SealServiceStatus.RequiresFullSession;

        var status = await SealBundleReader
            .ExtractBackupAsync(bundlePath, bundlePassword, destination, cancellationToken)
            .ConfigureAwait(false);

        return status switch
        {
            SealBundleReadStatus.Ok => SealServiceStatus.Ok,
            SealBundleReadStatus.VersionUnsupported => SealServiceStatus.VersionUnsupported,
            SealBundleReadStatus.WrongPassword => SealServiceStatus.WrongPassword,
            SealBundleReadStatus.Corrupt => SealServiceStatus.Corrupt,
            _ => SealServiceStatus.IoError,
        };
    }


    public Task<SealServiceStatus> ClearAsync(CancellationToken cancellationToken = default)
    {
        if (!IsUnlocked) return Task.FromResult(SealServiceStatus.NotUnlocked);
        if (Mode == SealSessionMode.Direct) return Task.FromResult(SealServiceStatus.RequiresFullSession);

        cancellationToken.ThrowIfCancellationRequested();



        if (!_writeGate.Wait(0)) return Task.FromResult(SealServiceStatus.Busy);
        try
        {
            return Task.FromResult(Map(_store.DeleteAll()));
        }
        finally
        {


            Lock();
            _writeGate.Release();
        }
    }







    public Task<SealServiceStatus> ClearWithoutSessionAsync(CancellationToken cancellationToken = default)
    {
        if (Mode == SealSessionMode.Direct) return Task.FromResult(SealServiceStatus.RequiresFullSession);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_writeGate.Wait(0)) return Task.FromResult(SealServiceStatus.Busy);
        try
        {

            return Task.FromResult(Map(SealIntegration.ClearPrivateSpace(_paths)));
        }
        finally
        {
            Lock();
            _writeGate.Release();
        }
    }










    private byte[]? TryVmkCopy()
    {



        lock (_gate)
        {
            var vmk = _session?.Vmk;
            return vmk is null ? null : vmk.AsSpan().ToArray();
        }
    }






    private static bool IsLoadFailure(Exception ex)
        => ex is SealCorruptException or SealVersionUnsupportedException
            or IOException or UnauthorizedAccessException;

    private static SealServiceStatus StatusOf(Exception ex) => ex switch
    {
        SealVersionUnsupportedException => SealServiceStatus.VersionUnsupported,
        SealCorruptException { Reason: SealCorruptReason.IndexFull } => SealServiceStatus.IndexFull,
        SealCorruptException => SealServiceStatus.Corrupt,
        _ => SealServiceStatus.IoError,
    };

    private static int AppVersionCode()
    {
        var version = typeof(SealService).Assembly.GetName().Version ?? new Version(1, 0, 0);
        return version.Major * 1000 + version.Minor;
    }

    private static SealServiceStatus Map(SealStoreOutcome outcome) => outcome switch
    {
        SealStoreOutcome.Ok => SealServiceStatus.Ok,
        SealStoreOutcome.AlreadyInitialized => SealServiceStatus.AlreadyInitialized,
        SealStoreOutcome.NotInitialized => SealServiceStatus.NotInitialized,
        SealStoreOutcome.WrongPassword => SealServiceStatus.WrongPassword,
        SealStoreOutcome.VersionUnsupported => SealServiceStatus.VersionUnsupported,
        SealStoreOutcome.WeakPassword => SealServiceStatus.WeakPassword,
        SealStoreOutcome.SameAsMainPassword => SealServiceStatus.SameAsMainPassword,
        SealStoreOutcome.Corrupt => SealServiceStatus.Corrupt,
        _ => SealServiceStatus.IoError,
    };

    private static SealServiceStatus MapImport(SealImportStatus status) => status switch
    {
        SealImportStatus.Ok => SealServiceStatus.Ok,
        SealImportStatus.Corrupt => SealServiceStatus.Corrupt,
        SealImportStatus.WrongPassword => SealServiceStatus.WrongPassword,
        SealImportStatus.VersionUnsupported => SealServiceStatus.VersionUnsupported,
        SealImportStatus.WeakPassword => SealServiceStatus.WeakPassword,
        SealImportStatus.SameAsMainPassword => SealServiceStatus.SameAsMainPassword,
        SealImportStatus.NotUnlocked => SealServiceStatus.NotUnlocked,
        _ => SealServiceStatus.IoError,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Lock();

        foreach (var gate in _entryGates.Values) gate.Dispose();
        _entryGates.Clear();
    }
}
