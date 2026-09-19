using Novara.Models;

namespace Novara.Services;






public static class SyncService
{










    public sealed record PairResult(
        bool Ok, string ReasonKey = "", string ServerMessage = "", string SpaceKeyBase64 = "")
    {
        public static PairResult Success(string spaceKeyBase64) => new(true, "", "", spaceKeyBase64);
        public static PairResult Local(string key) => new(false, key);
        public static PairResult Remote(string message) => new(false, "", message);
    }

    private static readonly object Gate = new();


    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    private static SyncState _state = new();
    private static string? _spaceKey;
    private static string? _deviceToken;
    private static bool _keyWrapStale;
    private static bool _credentialUnavailable;
    private static SyncApiClient? _client;
    private static Timer? _timer;
    private static int _busy;
    private static SyncStateStore? _store;
    private static long _savedEpoch;
    private static NovaraStore? _savedStore;


    private static SyncStateStore StateFile => _store ??= new SyncStateStore(
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(NovaraStore.DefaultFilePath)!, SyncStateStore.FileName));


    public static SyncState State { get { lock (Gate) return _state; } }


    public static bool IsReady { get { lock (Gate) return _spaceKey is not null && _deviceToken is not null; } }





    public static bool KeyWrapStale { get { lock (Gate) return _keyWrapStale; } }






    public static bool CredentialUnavailable { get { lock (Gate) return _credentialUnavailable; } }

    public static bool IsSyncing => Volatile.Read(ref _busy) != 0;


    public static event Action<SyncRoundOutcome>? RoundCompleted;








    public static void OnUnlocked()
    {
        var store = App.Store;
        if (store is not { IsLoaded: true } || !store.IsEncrypted)
        {
            OnLocked();
            return;
        }

        var state = StateFile.Load();



        var token = SyncCredentialVault.TryGet(state.SpaceId, out var credentialReadFailed);






        ResolveSpaceKey(state, store, out var spaceKey, out var keyWrapStale);

        lock (Gate)
        {
            _client?.Dispose();
            _client = null;
            _state = state;
            _spaceKey = spaceKey;
            _deviceToken = token;
            _keyWrapStale = keyWrapStale;




            _credentialUnavailable = (credentialReadFailed || token is null || spaceKey is null)
                && !keyWrapStale && !string.IsNullOrEmpty(state.SpaceId) && state.Enabled;
        }

        AttachStoreSaved(store);
        StartTimer();








        if (state is { RestorePendingConfirm: true, Enabled: true })
            Task.Run(() => ReminderScheduler.Reconcile(new Dictionary<Guid, DateTime>(),
                ReminderScheduler.Snapshot(App.Store?.Database)));



        if (state.Enabled && spaceKey is not null && token is not null) _ = SafeSyncNowAsync();
    }






    private static void ResolveSpaceKey(SyncState state, NovaraStore store, out string? spaceKey, out bool keyWrapStale)
    {
        spaceKey = null;
        keyWrapStale = false;
        if (!state.Enabled || string.IsNullOrEmpty(state.KeyWrapJson) || store.Password is not { } password) return;
        try { spaceKey = SyncKeyWrap.Unwrap(SyncJson.DeserializeKeyWrap(state.KeyWrapJson), password); }
        catch (InvalidDataException) { spaceKey = null; keyWrapStale = true; }
    }




    private static void RefreshSessionSecrets()
    {
        var store = App.Store;
        if (store is not { IsLoaded: true, IsEncrypted: true }) return;
        SyncState state;
        lock (Gate) state = _state;
        ResolveSpaceKey(state, store, out var spaceKey, out var keyWrapStale);
        lock (Gate)
        {
            _spaceKey = spaceKey;
            _keyWrapStale = keyWrapStale;
            _credentialUnavailable = state.Enabled && !string.IsNullOrEmpty(state.SpaceId)
                && !keyWrapStale && (_deviceToken is null || spaceKey is null);
        }
    }





    public static void OnLocked()
    {
        DetachStoreSaved();
        StopTimer();
        lock (Gate)
        {
            _client?.Dispose();
            _client = null;
            _spaceKey = null;
            _deviceToken = null;
            _keyWrapStale = false;
            _credentialUnavailable = false;
            _state = new SyncState();
        }
    }






















    public static async Task<PairResult> PairAsync(
        string serverUrl, string spaceId, string enrollmentSecret, string spaceKeyBase64,
        string deviceName, int frequencyMinutes = 5)
    {
        var store = App.Store;
        if (store is not { IsLoaded: true } || !store.IsEncrypted)
            return PairResult.Local("Sync_Pair_NeedsEncryptedVault");
        if (store.Password is not { } password)
            return PairResult.Local("Sync_Pair_NeedsEncryptedVault");


        var isCreate = string.IsNullOrWhiteSpace(spaceKeyBase64);
        var effectiveSpaceKey = isCreate ? "" : spaceKeyBase64.Trim();
        if (!isCreate && !SyncKeyWrap.IsWellFormedSpaceKey(effectiveSpaceKey))
            return PairResult.Local("Sync_Pair_BadSpaceKey");
        if (string.IsNullOrWhiteSpace(spaceId) || string.IsNullOrWhiteSpace(enrollmentSecret))
            return PairResult.Local("Sync_Pair_MissingFields");

        string baseUrl;
        try { baseUrl = SyncApiClient.NormalizeBaseUrl(serverUrl); }
        catch (ArgumentException) { return PairResult.Local("Sync_Pair_BadUrl"); }

        using var pairing = new SyncApiClient(baseUrl, spaceId);
        var registered = await pairing.RegisterDeviceAsync(enrollmentSecret, deviceName).ConfigureAwait(false);
        if (!registered.Success) return PairResult.Remote(registered.Message);

        var deviceId = registered.Value!.DeviceId;
        var token = registered.Value.DeviceToken;
        using var device = new SyncApiClient(baseUrl, spaceId, deviceId, token);



        var existing = await device.GetKeyWrapAsync().ConfigureAwait(false);
        var existingJson = existing.Success ? existing.Value?.Json : null;
        if (string.IsNullOrWhiteSpace(existingJson)) existingJson = null;

        string wrapJson;
        if (existingJson is not null)
        {
            if (isCreate)
            {




                var recovered = await Task.Run(() => TryUnwrapKeyWrap(existingJson, password)).ConfigureAwait(false);
                if (recovered is null)
                    return PairResult.Local("Sync_Pair_SpaceAlreadyExists");
                effectiveSpaceKey = recovered;
                wrapJson = existingJson;
            }
            else
            {



                wrapJson = await Task.Run(() => SyncJson.SerializeKeyWrap(SyncKeyWrap.Wrap(effectiveSpaceKey, password))).ConfigureAwait(false);
            }
        }
        else
        {

            if (isCreate) effectiveSpaceKey = SyncKeyWrap.CreateSpaceKey();
            wrapJson = await Task.Run(() => SyncJson.SerializeKeyWrap(SyncKeyWrap.Wrap(effectiveSpaceKey, password))).ConfigureAwait(false);
            var uploaded = await device.PutKeyWrapAsync(wrapJson, ifMatchVersion: 0).ConfigureAwait(false);
            if (!uploaded.Success) return PairResult.Remote(uploaded.Message);
        }



        if (!await Task.Run(() => SyncCredentialVault.Store(spaceId, token)).ConfigureAwait(false))
            return PairResult.Local("Sync_Pair_CredentialStoreFailed");

        var state = new SyncState
        {
            Enabled = true,
            ServerUrl = baseUrl,
            SpaceId = spaceId,
            DeviceId = deviceId,
            DeviceName = deviceName,
            FrequencyMinutes = frequencyMinutes,
            KeyWrapJson = wrapJson,
            BaseVersion = 0,
            LastSeenVersion = 0,
            LastPushedSha256 = "",
        };
        if (!TrySaveState(state)) return PairResult.Local("Sync_Pair_StateWriteFailed");

        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.Pair, 0, deviceName));

        lock (Gate)
        {
            _client?.Dispose();
            _client = null;
            _state = state;
            _spaceKey = effectiveSpaceKey;
            _deviceToken = token;
        }

        StartTimer();
        return PairResult.Success(effectiveSpaceKey);
    }






    private static string? TryUnwrapKeyWrap(string wrapJson, string password)
    {
        try { return SyncKeyWrap.Unwrap(SyncJson.DeserializeKeyWrap(wrapJson), password); }
        catch (InvalidDataException) { return null; }
        catch (ArgumentException) { return null; }
    }


    public static void Unpair()
    {
        SyncState state;
        lock (Gate) state = _state;


        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.Unpair));


        if (!SyncCredentialVault.Clear(state.SpaceId))
            SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.Error, 0, "device token not cleared", false));
        OnLocked();
        TrySaveState(new SyncState());
    }
















    public enum RewrapOutcome
    {
        Ok,
        OkLocalOnly,
        NotOurs,
        SessionTornDown,
        StateWriteFailed,
        RoundTripFailed,
    }

    public static async Task<RewrapOutcome> RewrapForKeyPasswordChangeAsync(string oldPassword, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword)) return RewrapOutcome.RoundTripFailed;

        SyncState state;
        lock (Gate) state = _state;
        if (string.IsNullOrEmpty(state.KeyWrapJson)) return RewrapOutcome.Ok;


        var previousWrapJson = state.KeyWrapJson;




        string? rewrapped;
        try
        {
            rewrapped = await Task.Run(() => SyncKeyWrap.Rewrap(previousWrapJson, oldPassword, newPassword)).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.Error, 0, "keywrap rewrap round-trip failed", false));
            return RewrapOutcome.RoundTripFailed;
        }
        if (rewrapped is null)
        {


            lock (Gate) _keyWrapStale = true;
            SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.Error, 0, "keywrap record is not ours", false));
            return RewrapOutcome.NotOurs;
        }












        lock (Gate)
        {
            if (!ReferenceEquals(_state, state))
            {
                SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.Error, 0, "session torn down during keywrap rewrap", false));
                return RewrapOutcome.SessionTornDown;
            }
            state.KeyWrapJson = rewrapped;
        }
        if (!TrySaveState(state))
        {
            SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.StateSaveFailed, 0, "keywrap rewrap", false));
            return RewrapOutcome.StateWriteFailed;
        }

        SyncApiClient? client;
        lock (Gate) client = _client;
        if (client is null)
        {



            SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.KeyRewrap, 0, "local only (no session client)", true));
            NotifyKeyWrapLocalOnly();
            return RewrapOutcome.OkLocalOnly;
        }

        var remote = await client.GetKeyWrapAsync().ConfigureAwait(false);


        if (!remote.Success || remote.Value is null ||
            !string.Equals(remote.Value.Json, previousWrapJson, StringComparison.Ordinal))
        {




            SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.KeyRewrap, 0, "local only (server record differs)", true));
            NotifyKeyWrapLocalOnly();
            return RewrapOutcome.OkLocalOnly;
        }

        var put = await client.PutKeyWrapAsync(rewrapped, remote.Value.Version).ConfigureAwait(false);
        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.KeyRewrap, 0, put.Success ? "local+remote" : "local only", put.Success));
        return put.Success ? RewrapOutcome.Ok : RewrapOutcome.RoundTripFailed;
    }






    private static void NotifyKeyWrapLocalOnly()
        => App.ShowToast(App.GetString("Sync_KeyRewrap_LocalOnly"));


    public static void SetEnabled(bool enabled)
    {
        SyncState state;
        lock (Gate)
        {
            _state.Enabled = enabled;
            state = _state;
        }
        TrySaveState(state);
        if (enabled)
        {


            RefreshSessionSecrets();
            StartTimer();
        }
        else StopTimer();
    }


    public static void SetFrequency(int minutes)
    {
        SyncState state;
        lock (Gate)
        {
            _state.FrequencyMinutes = minutes;
            state = _state;
        }
        TrySaveState(state);
    }







    public static async Task<SyncRoundOutcome> SyncNowAsync(bool force = false)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return new SyncRoundOutcome(SyncRoundStatus.NoOp, "a round is already running");



        var startEpoch = Volatile.Read(ref _savedEpoch);
        try
        {
            SyncApiClient client;
            SyncState state;
            string? spaceKey;
            string? token;
            lock (Gate)
            {
                state = _state;
                spaceKey = _spaceKey;
                token = _deviceToken;
                client = _client ??= BuildClient(state, token);
            }

            var store = App.Store;
            if (store is null)
                return Publish(new SyncRoundOutcome(SyncRoundStatus.WaitUnlocked, "no vault"));



            var remindersBefore = ReminderSnapshot(store);

            var outcome = force
                ? await SyncEngine.ForcePushAsync(store, state, spaceKey ?? "", client, DateTime.UtcNow).ConfigureAwait(false)
                : await SyncEngine.RunRoundAsync(store, state, spaceKey ?? "", client, DateTime.UtcNow).ConfigureAwait(false);




            if (outcome.Changed || outcome.NeedsDecision)
            {
                if (!TrySaveState(state))
                    SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.StateSaveFailed, outcome.RemoteVersion, "sync-state.json", ok: false));
            }





            if (outcome.Status == SyncRoundStatus.AutoRebased) ClearRestorePendingConfirm();
            RecordRound(state, outcome, force);
            return Publish(outcome, remindersBefore);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);




            if (IsEverySaveFrequency() && Volatile.Read(ref _savedEpoch) != startEpoch)
                _ = SafeSyncNowAsync();
        }
    }








    private static async Task SafeSyncNowAsync(bool force = false)
    {
        try { await SyncNowAsync(force).ConfigureAwait(false); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"同步轮次异常: {ex}");
            Publish(new SyncRoundOutcome(SyncRoundStatus.Error, ex.Message));
        }
    }


    public static Task<SyncRoundOutcome> SyncIfDueAsync(DateTime utcNow)
    {
        SyncState state;
        lock (Gate) state = _state;
        if (!state.Enabled || !IsReady) return Task.FromResult(new SyncRoundOutcome(SyncRoundStatus.Disabled));

        var minutes = state.FrequencyMinutes;
        if (minutes <= SyncState.FrequencyManual) return Task.FromResult(new SyncRoundOutcome(SyncRoundStatus.Disabled));
        if (state.LastSyncAt is { } last && (utcNow - last) < TimeSpan.FromMinutes(minutes))
            return Task.FromResult(new SyncRoundOutcome(SyncRoundStatus.NoOp, "not due yet"));

        return SyncNowAsync();
    }






    public static async Task<SyncRoundOutcome> SyncAfterSaveAsync()
    {



        try
        {
            SyncState state;
            string? spaceKey, token;
            lock (Gate)
            {
                state = _state;
                spaceKey = _spaceKey;
                token = _deviceToken;
            }



            if (state.FrequencyMinutes != SyncState.FrequencyEverySave
                || !state.Enabled || spaceKey is null || token is null)
                return new SyncRoundOutcome(SyncRoundStatus.Disabled);

            return await SyncNowAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SyncAfterSave 失败（已吞，避免未观察任务异常）: {ex}");
            return new SyncRoundOutcome(SyncRoundStatus.Error, "sync-after-save failed");
        }
    }

    private static bool IsEverySaveFrequency()
    {
        lock (Gate) return _state.FrequencyMinutes == SyncState.FrequencyEverySave;
    }



    private static void AttachStoreSaved(NovaraStore store)
    {
        lock (Gate)
        {
            if (ReferenceEquals(_savedStore, store)) return;
            if (_savedStore is not null) _savedStore.Saved -= OnStoreSaved;
            _savedStore = store;
            store.Saved += OnStoreSaved;
        }
    }

    private static void DetachStoreSaved()
    {
        lock (Gate)
        {
            if (_savedStore is null) return;
            _savedStore.Saved -= OnStoreSaved;
            _savedStore = null;
        }
    }






    private static void OnStoreSaved()
    {
        Interlocked.Increment(ref _savedEpoch);
        _ = SyncAfterSaveAsync();
    }




    public static string? SpaceKeyBase64 { get { lock (Gate) return _spaceKey; } }






    public static string? RevealSpaceKey(string lockPassword)
    {
        if (string.IsNullOrEmpty(lockPassword)) return null;

        SyncState state;
        lock (Gate) state = _state;
        if (string.IsNullOrEmpty(state.KeyWrapJson)) return null;

        try { return SyncKeyWrap.Unwrap(SyncJson.DeserializeKeyWrap(state.KeyWrapJson), lockPassword); }
        catch (InvalidDataException) { return null; }
        catch (ArgumentException) { return null; }
    }




    public sealed record SpaceKeyBackupOutcome(bool Ok, string Message, string SpaceKeyBase64 = "")
    {
        public static SpaceKeyBackupOutcome Success(string spaceKeyBase64 = "") => new(true, "", spaceKeyBase64);

        public static SpaceKeyBackupOutcome Fail(string i18nKey) => new(false, i18nKey);
    }










    public static SpaceKeyBackupOutcome ExportSpaceKeyFile(string path)
    {
        SyncState state;
        lock (Gate) state = _state;
        if (string.IsNullOrEmpty(state.KeyWrapJson)) return SpaceKeyBackupOutcome.Fail("Sync_KeyBackup_NotPaired");

        try { System.IO.File.WriteAllText(path, state.KeyWrapJson); }
        catch (IOException) { return SpaceKeyBackupOutcome.Fail("Sync_KeyBackup_WriteFailed"); }
        catch (UnauthorizedAccessException) { return SpaceKeyBackupOutcome.Fail("Sync_KeyBackup_WriteFailed"); }

        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.KeyExport));
        return SpaceKeyBackupOutcome.Success();
    }










    public static SpaceKeyBackupOutcome ImportSpaceKeyFile(string path, string password)
    {
        SyncState state;
        lock (Gate) state = _state;

        string json;
        try { json = System.IO.File.ReadAllText(path); }
        catch (IOException) { return SpaceKeyBackupOutcome.Fail("Sync_KeyBackup_ReadFailed"); }
        catch (UnauthorizedAccessException) { return SpaceKeyBackupOutcome.Fail("Sync_KeyBackup_ReadFailed"); }

        string spaceKey;
        try { spaceKey = SyncKeyWrap.Unwrap(SyncJson.DeserializeKeyWrap(json), password); }
        catch (InvalidDataException) { return SpaceKeyBackupOutcome.Fail("Sync_KeyBackup_OpenFailed"); }
        catch (ArgumentException) { return SpaceKeyBackupOutcome.Fail("Sync_KeyBackup_OpenFailed"); }

        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.KeyImport));
        return SpaceKeyBackupOutcome.Success(spaceKey);
    }




    public sealed record ReadTokenOutcome(bool Ok, bool Unreachable, string Token, string Message)
    {
        public static ReadTokenOutcome Success(string token) => new(true, false, token, "");

        public static ReadTokenOutcome Local(string i18nKey) => new(false, false, "", i18nKey);

        public static ReadTokenOutcome Failure(bool unreachable, string message) => new(false, unreachable, "", message);
    }






    public static async Task<ReadTokenOutcome> IssueReadTokenAsync(CancellationToken ct = default)
    {
        SyncApiClient? client;
        SyncState state;
        lock (Gate)
        {
            state = _state;
            client = IsReady ? _client ??= BuildClient(state, _deviceToken) : null;
        }
        if (client is null) return ReadTokenOutcome.Local("Sync_ReadAccess_NeedsUnlocked");

        var result = await client.IssueReadTokenAsync(ct).ConfigureAwait(false);
        if (!result.Success) return ReadTokenOutcome.Failure(result.TransportFailure, result.Message);

        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.ReadTokenCreate));
        return ReadTokenOutcome.Success(result.Value!);
    }


    public static async Task<(bool Ok, string Message)> RevokeReadTokenAsync(CancellationToken ct = default)
    {
        SyncApiClient? client;
        SyncState state;
        lock (Gate)
        {
            state = _state;
            client = IsReady ? _client ??= BuildClient(state, _deviceToken) : null;
        }
        if (client is null) return (false, "Sync_ReadAccess_NeedsUnlocked");

        var result = await client.RevokeReadTokenAsync(ct).ConfigureAwait(false);
        if (!result.Success) return (false, result.Message);

        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.ReadTokenRevoke));
        return (true, "");
    }





    public static async Task<(bool Ok, bool HasToken, string Message)> QueryReadTokenStateAsync(CancellationToken ct = default)
    {
        SyncApiClient? client;
        lock (Gate) client = IsReady ? _client ??= BuildClient(_state, _deviceToken) : null;
        if (client is null) return (false, false, "Sync_ReadAccess_NeedsUnlocked");

        var info = await client.GetInfoAsync(ct).ConfigureAwait(false);
        return info.Success ? (true, info.Value!.HasReadToken, "") : (false, false, info.Message);
    }







    public static async Task<ReadTokenOutcome> IssueEditorDeviceAsync(CancellationToken ct = default)
    {
        SyncApiClient? client;
        SyncState state;
        lock (Gate)
        {
            state = _state;
            client = IsReady ? _client ??= BuildClient(state, _deviceToken) : null;
        }
        if (client is null) return ReadTokenOutcome.Local("Sync_EditAccess_NeedsUnlocked");

        var result = await client.IssueEditorDeviceAsync(ct).ConfigureAwait(false);
        if (!result.Success) return ReadTokenOutcome.Failure(result.TransportFailure, result.Message);

        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.EditorDeviceCreate));
        return ReadTokenOutcome.Success(result.Value!.DeviceToken);
    }


    public static async Task<(bool Ok, string Message)> RevokeEditorDeviceAsync(CancellationToken ct = default)
    {
        SyncApiClient? client;
        SyncState state;
        lock (Gate)
        {
            state = _state;
            client = IsReady ? _client ??= BuildClient(state, _deviceToken) : null;
        }
        if (client is null) return (false, "Sync_EditAccess_NeedsUnlocked");

        var result = await client.RevokeEditorDeviceAsync(ct).ConfigureAwait(false);
        if (!result.Success) return (false, result.Message);

        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.EditorDeviceRevoke));
        return (true, "");
    }


    public static async Task<(bool Ok, bool HasDevice, string Message)> QueryEditorDeviceStateAsync(CancellationToken ct = default)
    {
        SyncApiClient? client;
        lock (Gate) client = IsReady ? _client ??= BuildClient(_state, _deviceToken) : null;
        if (client is null) return (false, false, "Sync_EditAccess_NeedsUnlocked");

        var info = await client.GetInfoAsync(ct).ConfigureAwait(false);
        return info.Success ? (true, info.Value!.HasEditorDevice, "") : (false, false, info.Message);
    }




    public sealed record DeviceListOutcome(bool Ok, bool Unreachable, string Message, List<DeviceInfo>? Devices)
    {
        public static DeviceListOutcome Success(List<DeviceInfo> devices) => new(true, false, "", devices);

        public static DeviceListOutcome Local(string i18nKey) => new(false, false, i18nKey, null);

        public static DeviceListOutcome Failure(bool unreachable, string message) => new(false, unreachable, message, null);
    }


    public static string CurrentDeviceId { get { lock (Gate) return _state.DeviceId ?? ""; } }


    public static async Task<DeviceListOutcome> ListDevicesAsync(CancellationToken ct = default)
    {
        SyncApiClient? client;
        lock (Gate) client = IsReady ? _client ??= BuildClient(_state, _deviceToken) : null;
        if (client is null) return DeviceListOutcome.Local("Sync_DeviceCenter_NeedsUnlocked");

        var result = await client.ListDevicesAsync(ct).ConfigureAwait(false);
        if (!result.Success) return DeviceListOutcome.Failure(result.TransportFailure, result.Message);

        var current = _state.DeviceId ?? "";
        var devices = result.Value!
            .OrderByDescending(d => d.LastSeenAt.HasValue)
            .ThenByDescending(d => d.LastSeenAt)
            .ThenBy(d => d.DeviceId == current ? 0 : 1)
            .ToList();
        foreach (var device in devices) device.Current = device.DeviceId == current;
        return DeviceListOutcome.Success(devices);
    }


    public static async Task<(bool Ok, string Message)> RevokeDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        SyncApiClient? client;
        SyncState state;
        lock (Gate)
        {
            if (deviceId == _state.DeviceId)
                return (false, "Sync_DeviceCenter_CannotRevokeSelf");
            state = _state;
            client = IsReady ? _client ??= BuildClient(state, _deviceToken) : null;
        }
        if (client is null) return (false, "Sync_DeviceCenter_NeedsUnlocked");

        var result = await client.RevokeDeviceAsync(deviceId, ct).ConfigureAwait(false);
        if (!result.Success) return (false, result.Message);

        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.DeviceRevoke, 0, deviceId));
        return (true, "");
    }


    public static async Task<(bool Ok, string Message)> ResetTokenAsync(string deviceId, CancellationToken ct = default)
    {
        SyncApiClient? client;
        SyncState state;
        lock (Gate)
        {
            state = _state;
            client = IsReady ? _client ??= BuildClient(state, _deviceToken) : null;
        }
        if (client is null) return (false, "Sync_DeviceCenter_NeedsUnlocked");

        var result = await client.ResetTokenAsync(deviceId, ct).ConfigureAwait(false);
        if (!result.Success) return (false, result.Message);

        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.DeviceReset, 0, deviceId));
        return (true, "");
    }






    public static ReadOnlyLink? BuildReadOnlyLink()
    {
        SyncState state;
        lock (Gate) state = _state;
        return ReadOnlyLink.Build(state.ServerUrl, state.SpaceId);
    }





    public static Task<SyncConflictInspection> InspectConflictAsync(CancellationToken ct = default)
    {
        SyncApiClient client;
        SyncState state;
        string? spaceKey;
        string? token;
        lock (Gate)
        {
            state = _state;
            spaceKey = _spaceKey;
            token = _deviceToken;
            client = _client ??= BuildClient(state, token);
        }

        var store = App.Store;
        if (store is null) return Task.FromResult(SyncConflictInspection.Fail("no vault"));
        return SyncConflictResolver.InspectAsync(store, state, spaceKey ?? "", client, ct);
    }




















    public static bool MarkRestorePendingConfirm()
    {
        try
        {
            var state = StateFile.Load();
            if (string.IsNullOrEmpty(state.SpaceId)) return true;
            if (state.RestorePendingConfirm) return true;
            state.RestorePendingConfirm = true;



            if (!TrySaveState(state))
            {
                SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.StateSaveFailed, 0, "restore pending confirm", ok: false));
                return false;
            }
            lock (Gate) { if (!string.IsNullOrEmpty(_state.SpaceId)) _state.RestorePendingConfirm = true; }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }




    public static void ClearRestorePendingConfirm()
    {
        try
        {
            var state = StateFile.Load();
            if (!state.RestorePendingConfirm) return;
            state.RestorePendingConfirm = false;
            if (!TrySaveState(state))
            {
                SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.StateSaveFailed, 0, "clear restore pending confirm", ok: false));
                return;
            }
            lock (Gate) _state.RestorePendingConfirm = false;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }



    public static async Task<SyncRoundOutcome> ResolveKeepLocalAsync()
    {
        var outcome = await SyncNowAsync(force: true).ConfigureAwait(false);

        if (outcome.Status == SyncRoundStatus.Push) ClearRestorePendingConfirm();
        return outcome;
    }






    public static async Task<SyncRoundOutcome> ResolveTakeRemoteAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return new SyncRoundOutcome(SyncRoundStatus.NoOp, "a round is already running");

        try
        {
            SyncApiClient client;
            SyncState state;
            string? spaceKey;
            string? token;
            lock (Gate)
            {
                state = _state;
                spaceKey = _spaceKey;
                token = _deviceToken;
                client = _client ??= BuildClient(state, token);
            }

            var store = App.Store;
            if (store is null) return Publish(new SyncRoundOutcome(SyncRoundStatus.WaitUnlocked, "no vault"));


            var remindersBefore = ReminderSnapshot(store);

            var outcome = await SyncEngine.PullAsync(store, state, spaceKey ?? "", client, DateTime.UtcNow, ct)
                .ConfigureAwait(false);
            if (outcome.Changed || outcome.NeedsDecision)
            {
                if (!TrySaveState(state))
                    SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.StateSaveFailed, 0, "sync-state.json", ok: false));
            }



            if (outcome.Status == SyncRoundStatus.Pull) ClearRestorePendingConfirm();



            SyncAuditLog.WriteOrdered(outcome.Status switch
            {
                SyncRoundStatus.Pull => Audit(state, SyncAuditEvents.TakeRemote, outcome.RemoteVersion),
                SyncRoundStatus.RollbackRejected => Audit(state, SyncAuditEvents.Rollback, outcome.RemoteVersion, outcome.Message, ok: false),
                _ => Audit(state, SyncAuditEvents.Error, outcome.RemoteVersion, outcome.Message, ok: false),
            });
            return Publish(outcome, remindersBefore);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }


    public static SyncExportOutcome ExportLocalVersion(string path)
    {
        var store = App.Store;
        if (store is null) return SyncExportOutcome.Fail("no vault");
        return SyncConflictResolver.ExportLocalVersion(path, store, SpaceKeyBase64 ?? "");
    }







    private static SyncAuditEvent Audit(SyncState state, string ev, long ver = 0, string detail = "", bool ok = true)
        => new()
        {
            Ev = ev,


            Ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Server = state.ServerUrl,
            Space = state.SpaceId,
            Device = state.DeviceName,
            Ver = ver,
            Detail = detail,
            Ok = ok,
        };










    private static void RecordRound(SyncState state, SyncRoundOutcome outcome, bool forced)
    {
        var ev = outcome.Status switch
        {
            SyncRoundStatus.Push => forced ? SyncAuditEvents.KeepLocal : SyncAuditEvents.Push,
            SyncRoundStatus.Pull => SyncAuditEvents.Pull,
            SyncRoundStatus.AutoRebased => SyncAuditEvents.AutoRebase,
            SyncRoundStatus.Conflict => SyncAuditEvents.Conflict,
            SyncRoundStatus.RollbackRejected => SyncAuditEvents.Rollback,
            SyncRoundStatus.Error => SyncAuditEvents.Error,
            _ => null,
        };
        if (ev is null) return;



        var detail = outcome.Status == SyncRoundStatus.Conflict ? $"local={outcome.LocalVersion}" : outcome.Message;
        var ok = outcome.Status is SyncRoundStatus.Push or SyncRoundStatus.Pull or SyncRoundStatus.AutoRebased;

        SyncAuditLog.WriteOrdered(Audit(state, ev, outcome.RemoteVersion, detail, ok));
    }


    public static void RecordExport(bool remote, long version)
    {
        SyncState state;
        lock (Gate) state = _state;
        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.Export, version, remote ? "remote" : "local"));
    }


    public static void RecordKeyViewed()
    {
        SyncState state;
        lock (Gate) state = _state;
        SyncAuditLog.WriteOrdered(Audit(state, SyncAuditEvents.KeyView));
    }

    private static SyncApiClient BuildClient(SyncState state, string? token)
        => new(state.ServerUrl, string.IsNullOrEmpty(state.SpaceId) ? "unpaired" : state.SpaceId,
            state.DeviceId, token);


    private static bool TrySaveState(SyncState state)
    {
        try { StateFile.Save(state); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static SyncRoundOutcome Publish(SyncRoundOutcome outcome, Dictionary<Guid, DateTime>? remindersBefore = null)
    {




        AfterRoundApplied(outcome, remindersBefore);
        try { RoundCompleted?.Invoke(outcome); } catch {  }
        return outcome;
    }





    private static Dictionary<Guid, DateTime>? ReminderSnapshot(NovaraStore store)
        => ReminderScheduler.Snapshot(store.Database);








    private static void ResyncReminders(Dictionary<Guid, DateTime> before)
        => ReminderScheduler.Reconcile(before, ReminderScheduler.Snapshot(App.Store?.Database));













    private static void AfterRoundApplied(SyncRoundOutcome outcome, Dictionary<Guid, DateTime>? remindersBefore = null)
    {
        if (outcome.Status == SyncRoundStatus.Pull)
        {
            SanitizePulledDiaryHtml();
            if (remindersBefore is not null) ResyncReminders(remindersBefore);
        }


        if (SyncViewRefresh.Required(outcome.Status))
            App.UiQueue?.TryEnqueue(() => App.MainWindow?.RefreshAfterSyncPull());
    }

    private static void SanitizePulledDiaryHtml()
    {
        var db = App.Store?.Database;
        if (db is null) return;
        var changed = false;
        foreach (var diary in db.DiaryItems)
        {
            if (diary.IsDeleted || string.Equals(diary.Format, "markdown", StringComparison.Ordinal)) continue;
            var clean = HtmlSanitizer.Sanitize(diary.Content ?? "");
            if (!string.Equals(clean, diary.Content, StringComparison.Ordinal)) { diary.Content = clean; changed = true; }
        }
        if (changed) { try { App.Store?.SaveSync(); } catch {  } }
    }

    private static void StartTimer()
    {
        lock (Gate)
        {
            _timer ??= new Timer(_ => _ = OnTickAsync(), null, Tick, Tick);
        }
    }

    private static void StopTimer()
    {
        Timer? timer;
        lock (Gate)
        {
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
    }

    private static async Task OnTickAsync()
    {
        try
        {
            var outcome = await SyncIfDueAsync(DateTime.UtcNow).ConfigureAwait(false);

            if (outcome.Changed || outcome.NeedsDecision) App.UiQueue?.TryEnqueue(() => App.MainWindow?.ShowToast(ToastFor(outcome)));

        }
        catch {  }
    }


    private static string ToastFor(SyncRoundOutcome outcome) => outcome.Status switch
    {
        SyncRoundStatus.Push => App.GetString("Sync_Toast_Pushed"),
        SyncRoundStatus.Pull => App.GetString("Sync_Toast_Pulled"),
        SyncRoundStatus.Conflict => App.GetString("Sync_Toast_Conflict"),
        SyncRoundStatus.RollbackRejected => App.GetString("Sync_Toast_Rollback"),
        _ => App.GetString("Sync_Toast_Done"),
    };
}
