namespace Novara.Models;

public class MemoGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string IconKey { get; set; } = "";
    public bool IsStarred { get; set; }
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; }
}
