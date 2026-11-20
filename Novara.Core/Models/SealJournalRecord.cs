using Novara.Services;

namespace Novara.Models;


public static class SealJournalOp
{
    public const string Seal = "seal";
    public const string Take = "take";
}





public static class SealJournalPhase
{
    public const string SealBegin = "seal_begin";
    public const string Sealed = "sealed";
    public const string SealIndexed = "seal_indexed";
    public const string Deleted = "deleted";
    public const string TakeBegin = "take_begin";
    public const string Taken = "taken";
}











public sealed class SealJournalDraft
{

    public string OriginalName { get; set; } = "";


    public string DisplayName { get; set; } = "";


    public ulong Size { get; set; }


    public long SealedAtUtc { get; set; }


    public string Kind { get; set; } = SealedItemKind.Normal;


    public long? ExpiryUtc { get; set; }


    public string? Note { get; set; }





    public string ContentSha256 { get; set; } = "";


    public int? FolderEntryCount { get; set; }

    internal static bool IsWellFormed(SealJournalDraft draft, bool requireHash, out string reason)
    {
        if (string.IsNullOrEmpty(draft.OriginalName)) { reason = "originalName is required"; return false; }
        if (string.IsNullOrEmpty(draft.DisplayName)) { reason = "displayName is required"; return false; }

        if (!string.Equals(draft.Kind, SealedItemKind.Normal, StringComparison.Ordinal)
            && !string.Equals(draft.Kind, SealedItemKind.Capsule, StringComparison.Ordinal))
        {
            reason = "kind must be normal or capsule";
            return false;
        }

        if (draft.SealedAtUtc < SealFormat.MinSealedAtUtc || draft.SealedAtUtc > SealFormat.MaxSealedAtUtc)
        {
            reason = "sealedAtUtc is outside the representable window";
            return false;
        }

        if (string.Equals(draft.Kind, SealedItemKind.Capsule, StringComparison.Ordinal))
        {
            if (draft.ExpiryUtc is null) { reason = "a capsule needs an expiry"; return false; }
            if (draft.ExpiryUtc <= draft.SealedAtUtc) { reason = "expiry must follow the seal moment"; return false; }
            if (draft.ExpiryUtc > SealFormat.MaxSealedAtUtc) { reason = "expiry is outside the representable window"; return false; }
        }
        else if (draft.ExpiryUtc is not null)
        {
            reason = "a normal item must not carry an expiry";
            return false;
        }

        if (draft.FolderEntryCount is < 0) { reason = "folderEntryCount cannot be negative"; return false; }

        if (draft.ContentSha256.Length > 0 && !IsHex(draft.ContentSha256, 64))
        {
            reason = "contentSha256 must be 64 hex characters when present";
            return false;
        }

        if (requireHash && draft.ContentSha256.Length == 0)
        {
            reason = "contentSha256 is required once the container has been written";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool IsHex(string? value, int length)
        => value is not null
           && value.Length == length
           && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

















public sealed class SealJournalRecord
{

    public string OpId { get; set; } = "";


    public string Op { get; set; } = SealJournalOp.Seal;


    public string Phase { get; set; } = SealJournalPhase.SealBegin;


    public string ItemId { get; set; } = "";


    public string SourcePath { get; set; } = "";


    public string? TargetPath { get; set; }


    public long StartedAtUtc { get; set; }


    public SealJournalDraft? Draft { get; set; }






    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMalformed { get; internal set; }


    [System.Text.Json.Serialization.JsonIgnore]
    public string MalformedReason { get; internal set; } = "";






    internal static bool IsWellFormed(SealJournalRecord record, out string reason)
    {
        if (!IsHex(record.OpId, 32)) { reason = "opId must be 32 hex characters"; return false; }
        if (!IsHex(record.ItemId, 32)) { reason = "itemId must be 32 hex characters"; return false; }
        if (string.IsNullOrEmpty(record.SourcePath)) { reason = "sourcePath is required"; return false; }

        if (record.StartedAtUtc < SealFormat.MinSealedAtUtc || record.StartedAtUtc > SealFormat.MaxSealedAtUtc)
        {
            reason = "startedAtUtc is outside the representable window";
            return false;
        }

        if (!PhaseBelongsToOp(record.Phase, record.Op))
        {
            reason = "phase does not belong to op";
            return false;
        }

        if (string.Equals(record.Op, SealJournalOp.Take, StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(record.TargetPath)) { reason = "a take record needs a targetPath"; return false; }
            if (record.Draft is not null) { reason = "a take record must not carry a draft"; return false; }
            reason = "";
            return true;
        }

        if (record.TargetPath is not null) { reason = "a seal record must not carry a targetPath"; return false; }

        var draftRequired = !string.Equals(record.Phase, SealJournalPhase.SealBegin, StringComparison.Ordinal);
        if (record.Draft is null)
        {
            if (draftRequired) { reason = "this phase needs the draft metadata"; return false; }
            reason = "";
            return true;
        }

        var requireHash = draftRequired;
        if (!SealJournalDraft.IsWellFormed(record.Draft, requireHash, out reason)) return false;

        reason = "";
        return true;
    }

    private static bool PhaseBelongsToOp(string phase, string op)
    {
        if (string.Equals(op, SealJournalOp.Seal, StringComparison.Ordinal))
            return phase is SealJournalPhase.SealBegin or SealJournalPhase.Sealed
                or SealJournalPhase.SealIndexed or SealJournalPhase.Deleted;

        if (string.Equals(op, SealJournalOp.Take, StringComparison.Ordinal))
            return phase is SealJournalPhase.TakeBegin or SealJournalPhase.Taken;

        return false;
    }

    private static bool IsHex(string? value, int length)
        => value is not null
           && value.Length == length
           && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
