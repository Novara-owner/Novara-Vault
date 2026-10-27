namespace Novara.Services;






public static class PlanCollapsePolicy
{

    public const int NotePreviewLines = 5;


    public const int TodoPreviewRows = 2;


    public const double TodoPreviewRowHeight = 32;




    public const double FoldDurationMs = 280;


    public static bool ShouldShowTodoExpand(int subCount) => subCount > TodoPreviewRows;


    public static bool ShouldShowNoteExpand(bool isTrimmed) => isTrimmed;
}
