namespace Novara.Models;

public class FilePathEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Note { get; set; } = "";
    public bool IsStarred { get; set; }
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime DeletedAt { get; set; }
    public string WorkspaceId { get; set; } = ""; // 9.3 Workspace: owning workspace id (empty = unassigned)
}
