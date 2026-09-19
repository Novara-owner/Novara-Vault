namespace Novara.Models;

public class DiaryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;
    public bool IsPinned { get; set; }
    public DateTime? PinnedAt { get; set; }
    public bool IsStarred { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime DeletedAt { get; set; }
    public int Order { get; set; }
    public string Format { get; set; } = "html";
    public string WorkspaceId { get; set; } = "";
}
