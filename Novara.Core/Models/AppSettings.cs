using System.Text.Json.Serialization;

namespace Novara.Models;

public class AppSettings
{
    public string Theme { get; set; } = "跟随系统";
    public bool AutoStart { get; set; }
    public bool ContextMenu { get; set; } = true;
    public string CloseBehavior { get; set; } = "直接退出";
    public List<string> VisibleTabs { get; set; } = new() { "备忘", "文件", "计划", "日记" };
    public bool PrivacyLockEnabled { get; set; }
    
    public int AutoLockSeconds { get; set; }
    public bool AutoLockOnSystemLock { get; set; }
    
    public bool QuickCaptureEnabled { get; set; } = false;
    public string QuickCaptureHotkey { get; set; } = "CtrlShiftN";
    public bool GcmMigrationRejected { get; set; } 
    public bool KdfMigrationRejected { get; set; } // 9.2#7: user declined the one-time v2->v3 KDF-hardening migration; symmetric with GcmMigrationRejected
    public string AppLanguage { get; set; } = ""; // zh-CN / en-US / zh-TW / ko-KR / ja-JP; empty = follow system (contract, see 2.1)
    public bool HasCompletedWelcome { get; set; }
    public bool HasCompletedCarousel { get; set; } // N4-01: 9.3 promo carousel "played once" flag - independent from HasCompletedWelcome (whose write happens before MaybeShowWelcomeCarousel and made the old gate unreachable in Release)
    public bool WelcomeOnLaunch { get; set; } = true; 
    
    public bool BackupEnabled { get; set; }
    public bool AutoBackupEnabled { get; set; } 
    public int BackupFreq { get; set; } // 0=30m, 1=1h, 2=6h, 3=1d
    
    public bool StatsEnabled { get; set; }
    
    public bool McpEnabled { get; set; }
    public string McpToken { get; set; } = "";
    public DateTime? McpTokenGeneratedAt { get; set; }
    public List<string> McpAllowedProcesses { get; set; } = new();
    public bool McpDeleteEnabled { get; set; }
    public bool McpDetailExpanded { get; set; } = true;
    
    
    public List<McpClientPermRecord> McpClientPermissions { get; set; } = new();
    public bool McpPermMigrated { get; set; } 
    
    public List<WorkspaceItem> Workspaces { get; set; } = new();
}

/// <summary>9.2#5: one authorized client's fine-grained permission set (McpPermissions flag bits).</summary>
public sealed class McpClientPermRecord
{
    public string Path { get; set; } = "";
    public long Permissions { get; set; } // McpPerm flags as long (JSON-friendly)
}
