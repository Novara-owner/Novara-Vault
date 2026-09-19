namespace Novara.Services;













public static class SyncViewRefresh
{

    public static bool Required(SyncRoundStatus status) =>
        status == SyncRoundStatus.Pull || status == SyncRoundStatus.AutoRebased;
}
