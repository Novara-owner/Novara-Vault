using Novara.Services;

namespace Novara.Models;


public static class SealedItemKind
{
    public const string Normal = "normal";
    public const string Capsule = "capsule";
}















public sealed class SealedItem
{

    public string Id { get; set; } = "";


    public string DisplayName { get; set; } = "";


    public string OriginalName { get; set; } = "";


    public string SourcePath { get; set; } = "";


    public ulong Size { get; set; }


    public long SealedAtUtc { get; set; }


    public string Kind { get; set; } = SealedItemKind.Normal;


    public long? ExpiryUtc { get; set; }


    public string? Note { get; set; }


    public string ContentSha256 { get; set; } = "";


    public int? FolderEntryCount { get; set; }


    public int Flags { get; set; }

    public bool IsCapsule => string.Equals(Kind, SealedItemKind.Capsule, StringComparison.Ordinal);

    public bool IsFolder => FolderEntryCount is not null;






    internal static bool IsWellFormed(SealedItem item, out string reason)
    {
        if (!IsHex(item.Id, 32)) { reason = "id must be 32 hex characters"; return false; }
        if (!IsHex(item.ContentSha256, 64)) { reason = "contentSha256 must be 64 hex characters"; return false; }
        if (string.IsNullOrEmpty(item.OriginalName)) { reason = "originalName is required"; return false; }
        if (string.IsNullOrEmpty(item.DisplayName)) { reason = "displayName is required"; return false; }

        if (!string.Equals(item.Kind, SealedItemKind.Normal, StringComparison.Ordinal)
            && !string.Equals(item.Kind, SealedItemKind.Capsule, StringComparison.Ordinal))
        {
            reason = "kind must be normal or capsule";
            return false;
        }

        if (item.SealedAtUtc < SealFormat.MinSealedAtUtc || item.SealedAtUtc > SealFormat.MaxSealedAtUtc)
        {
            reason = "sealedAtUtc is outside the representable window";
            return false;
        }

        if (item.IsCapsule)
        {
            if (item.ExpiryUtc is null) { reason = "a capsule needs an expiry"; return false; }
            if (item.ExpiryUtc <= item.SealedAtUtc) { reason = "expiry must follow the seal moment"; return false; }
            if (item.ExpiryUtc > SealFormat.MaxSealedAtUtc) { reason = "expiry is outside the representable window"; return false; }
        }
        else if (item.ExpiryUtc is not null)
        {
            reason = "a normal item must not carry an expiry";
            return false;
        }

        if (item.FolderEntryCount is < 0) { reason = "folderEntryCount cannot be negative"; return false; }
        if (item.Flags != 0) { reason = "flags must be zero in v1"; return false; }

        reason = "";
        return true;
    }






    private static bool IsHex(string? value, int length)
        => value is not null
           && value.Length == length
           && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
