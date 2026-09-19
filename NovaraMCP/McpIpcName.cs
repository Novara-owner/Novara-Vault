using System.Diagnostics;
using System.Globalization;

namespace Novara.Ipc;

public static class McpIpcName
{
#if DEBUG
    private const string Base = "Novara.Mcp.Dev";
#else
    private const string Base = "Novara.Mcp";
#endif


    public const string LegacyName = Base;


    public static readonly string Current = Base + "." + SessionTag();

    private static string SessionTag()
    {


        try { return Process.GetCurrentProcess().SessionId.ToString(CultureInfo.InvariantCulture); }
        catch { return "0"; }
    }
}
