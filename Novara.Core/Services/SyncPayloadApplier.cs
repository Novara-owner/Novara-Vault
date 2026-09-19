using System.Text.Json;
using Novara.Models;

namespace Novara.Services;


public sealed record SyncApplyOutcome(bool Success, LoadStatus Status, string Message)
{
    public static SyncApplyOutcome Ok() => new(true, LoadStatus.Ok, "");
    public static SyncApplyOutcome Fail(LoadStatus status, string message) => new(false, status, message);
}














public static class SyncPayloadApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };





    public static SyncApplyOutcome ApplyEnvelope(NovaraStore store, string envelopeJson, string spaceKeyBase64)
    {
        ArgumentNullException.ThrowIfNull(store);

        SyncEnvelope envelope;
        try { envelope = SyncEnvelopeCodec.Deserialize(envelopeJson); }
        catch (InvalidDataException e) { return SyncApplyOutcome.Fail(LoadStatus.Corrupted, e.Message); }

        bool authentic;
        try { authentic = SyncEnvelopeCodec.Verify(envelope, spaceKeyBase64); }
        catch (ArgumentException e) { return SyncApplyOutcome.Fail(LoadStatus.Corrupted, e.Message); }
        if (!authentic) return SyncApplyOutcome.Fail(LoadStatus.Corrupted, "the downloaded envelope failed verification");

        byte[] container;
        try { container = SyncEnvelopeCodec.DecodePayload(envelope); }
        catch (InvalidDataException e) { return SyncApplyOutcome.Fail(LoadStatus.Corrupted, e.Message); }

        return ApplyContainer(store, container, spaceKeyBase64);
    }





    public static SyncApplyOutcome ApplyContainer(NovaraStore store, byte[] container, string spaceKeyBase64)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(container);
        if (!SyncKeyWrap.IsWellFormedSpaceKey(spaceKeyBase64))
            return SyncApplyOutcome.Fail(LoadStatus.Corrupted, "space key is malformed");

        if (!store.IsLoaded) return SyncApplyOutcome.Fail(LoadStatus.NeedPassword, "locked");
        if (!store.IsEncrypted) return SyncApplyOutcome.Fail(LoadStatus.IoError, "sync requires an encrypted vault");
        if (store.IsSaveSuppressed)
            return SyncApplyOutcome.Fail(LoadStatus.IoError, NovaraStore.RestorePendingMessage);



        var localSettings = store.Database.AppSettings ?? new AppSettings();

        var tempFile = Path.Combine(Path.GetTempPath(), "novara-sync-" + Guid.NewGuid().ToString("N") + ".novaenc");
        try
        {
            File.WriteAllBytes(tempFile, container);
            var result = store.ImportBackup(tempFile, spaceKeyBase64);
            if (result.Status != LoadStatus.Ok)
                return SyncApplyOutcome.Fail(result.Status, result.Detail ?? result.Status.ToString());


            var incoming = store.Database.AppSettings ?? new AppSettings();
            SyncFieldPolicy.ApplyRoamingSettings(localSettings, incoming);
            localSettings.PrivacyLockEnabled = store.IsEncrypted;
            store.Database.AppSettings = localSettings;



            if (!store.SaveSync())
                return SyncApplyOutcome.Fail(LoadStatus.IoError, "the merged settings did not reach disk");

            return SyncApplyOutcome.Ok();
        }
        catch (IOException e) { return SyncApplyOutcome.Fail(LoadStatus.IoError, e.Message); }
        catch (UnauthorizedAccessException e) { return SyncApplyOutcome.Fail(LoadStatus.IoError, e.Message); }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch {  }
        }
    }


    public static byte[] BuildPayloadJson(NovaraDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return JsonSerializer.SerializeToUtf8Bytes(SyncFieldPolicy.CloneForSync(database), JsonOptions);
    }







    public static bool TryBuildStablePayloadJson(NovaraDatabase database, out byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(database);
        var stable = StableJson.SerializeToUtf8BytesStable(SyncFieldPolicy.CloneForSync(database), JsonOptions);
        if (stable == null) { payload = Array.Empty<byte>(); return false; }
        payload = stable;
        return true;
    }
}
