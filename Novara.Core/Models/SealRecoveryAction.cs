using Novara.Services;

namespace Novara.Models;





public enum SealRecoveryActionKind
{

    None = 0,


    DiscardStaging,


    OfferRetrySeal,


    StagingOnly,


    SourceLost,


    CompleteSealFromJournal,


    OrphanSourceRemains,


    FinalizeSeal,


    AskCleanupPartialTarget,


    BothSidesExist,


    FinalizeTake,
}





public enum SealTargetState
{

    Missing = 0,


    Partial,


    Complete,
}


public sealed record SealRecoveryAction(SealRecoveryActionKind Kind, string OpId, string ItemId)
{





    public bool NeedsUserDecision => Kind is SealRecoveryActionKind.OrphanSourceRemains
        or SealRecoveryActionKind.BothSidesExist
        or SealRecoveryActionKind.AskCleanupPartialTarget
        or SealRecoveryActionKind.OfferRetrySeal
        or SealRecoveryActionKind.StagingOnly
        or SealRecoveryActionKind.SourceLost;


    public bool IsAutomatic => Kind is SealRecoveryActionKind.DiscardStaging
        or SealRecoveryActionKind.CompleteSealFromJournal
        or SealRecoveryActionKind.FinalizeSeal
        or SealRecoveryActionKind.FinalizeTake;
}






public interface ISealFileProbe
{

    bool SourceExists(string sourcePath);


    bool ContainerExists(string itemId);


    SealTargetState TargetState(string targetPath, string itemId);



    bool FileExists(string path);
}






public sealed record SealRecoveryEntry(
    SealRecoveryAction Action,
    string? SourcePath,
    string? TargetPath,
    string DisplayName);


public enum SealRecoveryChoice
{
    None = 0,


    TryDeleteSource,


    SourceAlreadyGone,


    KeepTarget,


    KeepBoxEntry,


    KeepBoth,


    CleanupPartialTarget,


    KeepPartialTarget,


    DiscardStaging,
}









public sealed class SealRecoveryPlan
{
    internal SealRecoveryPlan(
        SealJournalStatus journalStatus,
        bool hasMalformedRecords,
        IReadOnlyList<SealRecoveryAction> actions)
    {
        JournalStatus = journalStatus;
        HasMalformedRecords = hasMalformedRecords;
        Actions = actions;
    }

    public SealJournalStatus JournalStatus { get; }


    public bool HasMalformedRecords { get; }

    public IReadOnlyList<SealRecoveryAction> Actions { get; }





    public bool JournalNeedsAttention => JournalStatus != SealJournalStatus.Ok;


    public bool HasPendingDecisions => Actions.Any(a => a.NeedsUserDecision);
}
