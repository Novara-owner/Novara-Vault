namespace Novara.Services;






public static class MemoDisplayMode
{
    public const string Auto = "auto";
    public const string Fixed = "fixed";
    public const string Memory = "memory";


    public static string Normalize(string? mode)
        => string.Equals(mode, Fixed, StringComparison.OrdinalIgnoreCase) ? Fixed
         : string.Equals(mode, Memory, StringComparison.OrdinalIgnoreCase) ? Memory
         : Auto;
}
