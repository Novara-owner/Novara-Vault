namespace Novara.Models;

/// <summary>9.3 Workspace: a virtual grouping that spans all five content partitions.
/// It adds no storage layer - entities just carry a WorkspaceId reference (empty = unassigned).</summary>
public class WorkspaceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string IconKey { get; set; } = ""; // IconData.GroupXX (reuses the memo group icon set)
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
