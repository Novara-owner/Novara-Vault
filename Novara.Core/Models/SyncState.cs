namespace Novara.Models;







public sealed class SyncState
{

    public const int FrequencyManual = 0;


    public const int FrequencyEverySave = -1;

    public bool Enabled { get; set; }


    public string ServerUrl { get; set; } = "";

    public string SpaceId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";


    public int FrequencyMinutes { get; set; } = 5;






    public long LastSeenVersion { get; set; }


    public long BaseVersion { get; set; }


    public string LastPushedSha256 { get; set; } = "";

    public DateTime? LastSyncAt { get; set; }





    public string KeyWrapJson { get; set; } = "";












    public bool RestorePendingConfirm { get; set; }
}
