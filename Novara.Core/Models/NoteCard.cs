namespace Novara.Models;

public class NoteCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string IconKey { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsStarred { get; set; }
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsExpanded { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime DeletedAt { get; set; }
    public int Order { get; set; } 
    public DateTime? ReminderAt { get; set; }   
    public DateTime? ReminderSetAt { get; set; } 
}
