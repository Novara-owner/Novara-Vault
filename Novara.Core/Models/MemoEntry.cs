namespace Novara.Models;

public class MemoEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? GroupId { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string KeyInfo { get; set; } = "";
    public List<EntryField> Fields { get; set; } = new();
    public bool IsStarred { get; set; }
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime DeletedAt { get; set; }
    public string Protocol { get; set; } = "";
    /// <summary>4.0: custom-type entries can pick their own icon. Empty = legacy behaviour (the
    
    public string IconKey { get; set; } = "";
    public string WorkspaceId { get; set; } = ""; // 9.3 Workspace: owning workspace id (empty = unassigned); MemoGroup stays workspace-less
}
