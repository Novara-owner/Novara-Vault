namespace Novara.Models;

public class TodoCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string IconKey { get; set; } = "";
    public string MainText { get; set; } = "";
    public List<string> SubTexts { get; set; } = new();
    public List<bool> CheckedStates { get; set; } = new();
    public bool IsStarred { get; set; }
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime DeletedAt { get; set; }
    public int Order { get; set; } 
    public DateTime? ReminderAt { get; set; }   
    public DateTime? ReminderSetAt { get; set; } 
    public string WorkspaceId { get; set; } = ""; // 9.3 Workspace: owning workspace id (empty = unassigned)
}
