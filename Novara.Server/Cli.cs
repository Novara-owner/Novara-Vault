using Novara.Sync.Server;

namespace Novara.Server;


public static class Cli
{
    public static int RunSpaceCommand(SpaceService service, string[] args)
    {
        if (args.Length < 2 || !args[1].Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("usage: NovaraSync space create [--name <name>]");
            return 2;
        }

        var name = "novara";
        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--name", StringComparison.OrdinalIgnoreCase)) name = args[i + 1];
        }

        var result = service.CreateSpace(name);
        if (!result.Success)
        {
            Console.Error.WriteLine($"failed: {result.Message}");
            return 1;
        }

        var credentials = result.Value!;
        Console.WriteLine("Novara sync space created. These values are shown once - store them safely.");
        Console.WriteLine();
        Console.WriteLine($"  space id           : {credentials.SpaceId}");
        Console.WriteLine($"  enrollment secret  : {credentials.EnrollmentSecret}");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("  1. In Novara (PC) -> Settings -> Connected: enter the server URL, the space id and");
        Console.WriteLine("     the enrollment secret. Leave the space key empty - the PC generates it and this");
        Console.WriteLine("     server never sees it. The app shows it once, and can reveal it again later.");
        Console.WriteLine("  2. Extra devices join with the same space id + enrollment secret, plus the space key");
        Console.WriteLine("     taken from the device that created the space (Settings -> view space key).");
        return 0;
    }
}
