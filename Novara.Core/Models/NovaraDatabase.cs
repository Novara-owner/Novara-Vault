namespace Novara.Models;

public class NovaraDatabase
{
    public List<DiaryEntry> DiaryItems { get; set; } = new();
    public List<FilePathEntry> PathBackupItems { get; set; } = new();
    public List<TodoCard> TodoCards { get; set; } = new();
    public List<NoteCard> NoteCards { get; set; } = new();
    public List<MemoGroup> MemoGroups { get; set; } = new();
    public List<MemoEntry> MemoEntries { get; set; } = new();
    public AppSettings AppSettings { get; set; } = new();
}
