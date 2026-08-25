namespace Novara.Services;

/// <summary>


/// </summary>
public static class CoreEnv
{
    public static string DataDirName =>
#if DEBUG
        "Novara-Dev";
#else
        "Novara";
#endif
}
