namespace Novara.Models;



public class WorkspaceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string IconKey { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
