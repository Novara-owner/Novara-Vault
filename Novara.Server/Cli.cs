using System.Globalization;
using System.Text;
using System.Text.Json;
using Novara.Sync.Server;

namespace Novara.Server;











public static class Cli
{
    private static readonly JsonSerializerOptions JsonOut = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static int RunSpaceCommand(SpaceService service, string[] args)
    {
        UseUtf8Output();

        if (args.Length < 2)
        {
            PrintUsage(Console.Error);
            return 2;
        }

        var rest = args[2..];
        return args[1].ToLowerInvariant() switch
        {
            "create" => Create(service, rest),
            "list" => List(service, rest),
            "show" => Show(service, rest),
            "delete" => Delete(service, rest),
            "rotate-secret" => RotateSecret(service, rest),
            _ => UnknownCommand(args[1]),
        };
    }



    private static int Create(SpaceService service, string[] args)
    {
        var parsed = Parse(args);
        if (parsed.Unknown.Count > 0) return RejectUnknown(parsed);
        if (parsed.Positional is not null) return RejectExtra(parsed.Positional, "space create");

        var result = service.CreateSpace(parsed.Name ?? "novara");
        if (!result.Success) return Fail(result.Message);

        var credentials = result.Value!;
        if (parsed.Json)
        {
            WriteJson(new { credentials.SpaceId, credentials.EnrollmentSecret });
            return 0;
        }

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
        Console.WriteLine();
        Console.WriteLine("  Run 'NovaraSync space list' to see every space on this server.");
        return 0;
    }

    private static int List(SpaceService service, string[] args)
    {
        var parsed = Parse(args);
        if (parsed.Unknown.Count > 0) return RejectUnknown(parsed);
        if (parsed.Positional is not null) return RejectExtra(parsed.Positional, "space list");

        var result = service.ListSpaces();
        if (!result.Success) return Fail(result.Message);

        var spaces = result.Value!;
        if (parsed.Json)
        {
            WriteJson(spaces);
            return 0;
        }

        if (spaces.Count == 0)
        {
            Console.WriteLine("No spaces on this server yet.");
            Console.WriteLine();
            Console.WriteLine("Create one with:");
            Console.WriteLine("  NovaraSync space create --name <name>");
            return 0;
        }

        var rows = new List<string[]>
        {
            new[] { "SPACE ID", "NAME", "VER", "DEVICES", "USED", "QUOTA", "CREATED (UTC)" },
        };
        foreach (var space in spaces)
        {
            rows.Add(new[]
            {
                space.SpaceId,
                space.Name,
                space.CurrentVersion.ToString(CultureInfo.InvariantCulture),
                DescribeDevices(space),
                FormatBytes(space.UsedBytes),
                space.QuotaBytes > 0 ? FormatBytes(space.QuotaBytes) : "unlimited",
                FormatTime(space.CreatedAt),
            });
        }

        PrintTable(rows);
        Console.WriteLine();
        Console.WriteLine($"{spaces.Count} space(s). Run 'NovaraSync space show <space id>' for detail.");
        return 0;
    }

    private static int Show(SpaceService service, string[] args)
    {
        var parsed = Parse(args);
        if (parsed.Unknown.Count > 0) return RejectUnknown(parsed);
        if (parsed.Positional is null) return RejectMissing("space show", "<space id>");

        var result = service.DescribeSpace(parsed.Positional);
        if (!result.Success) return Fail(result.Message);

        var space = result.Value!;
        if (parsed.Json)
        {
            WriteJson(space);
            return 0;
        }

        Console.WriteLine($"Space {space.SpaceId}");
        Console.WriteLine();
        PrintTable(new List<string[]>
        {
            new[] { "name", space.Name },
            new[] { "created", FormatTime(space.CreatedAt) },
            new[] { "updated", FormatTime(space.UpdatedAt) },
            new[] { "current version", space.CurrentVersion.ToString(CultureInfo.InvariantCulture) },
            new[] { "keywrap version", space.KeyWrapVersion.ToString(CultureInfo.InvariantCulture) },
            new[] { "devices", DescribeDevices(space) },
            new[] { "read-only token", space.HasReadToken ? "configured" : "not configured" },
            new[] { "web editor", space.HasEditorDevice ? "configured" : "not configured" },
            new[] { "used", space.QuotaBytes > 0
                ? $"{FormatBytes(space.UsedBytes)} of {FormatBytes(space.QuotaBytes)}"
                : $"{FormatBytes(space.UsedBytes)} (no quota)" },
            new[] { "max versions", space.MaxVersions.ToString(CultureInfo.InvariantCulture) },
        }, indent: "  ");
        return 0;
    }

    private static int Delete(SpaceService service, string[] args)
    {
        var parsed = Parse(args);
        if (parsed.Unknown.Count > 0) return RejectUnknown(parsed);
        if (parsed.Positional is null) return RejectMissing("space delete", "<space id> --yes");




        if (!parsed.Yes)
        {
            var preview = service.DescribeSpace(parsed.Positional);
            if (!preview.Success) return Fail(preview.Message);

            var space = preview.Value!;
            Console.Error.WriteLine("refusing to delete without --yes.");
            Console.Error.WriteLine();
            Console.Error.WriteLine($"This would permanently delete space {space.SpaceId} ({space.Name}) and everything in it:");
            Console.Error.WriteLine($"  versions   : {space.CurrentVersion} (current)");
            Console.Error.WriteLine($"  devices    : {DescribeDevices(space)}");
            Console.Error.WriteLine($"  data       : {FormatBytes(space.UsedBytes)}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("The payloads are ciphertext this server cannot reconstruct - the data is gone for good.");
            Console.Error.WriteLine("Re-run with --yes to confirm:");
            Console.Error.WriteLine($"  NovaraSync space delete {space.SpaceId} --yes");
            return 2;
        }

        var result = service.DeleteSpace(parsed.Positional);
        if (!result.Success) return Fail(result.Message);

        var deleted = result.Value!;
        if (parsed.Json)
        {
            WriteJson(new { deleted = deleted.SpaceId, deleted.Name, releasedBytes = deleted.UsedBytes });
            return 0;
        }

        Console.WriteLine($"Deleted space {deleted.SpaceId} ({deleted.Name}).");
        Console.WriteLine($"  versions   : {deleted.CurrentVersion} (current)");
        Console.WriteLine($"  devices    : {DescribeDevices(deleted)}");
        Console.WriteLine($"  reclaimed  : {FormatBytes(deleted.UsedBytes)}");
        return 0;
    }

    private static int RotateSecret(SpaceService service, string[] args)
    {
        var parsed = Parse(args);
        if (parsed.Unknown.Count > 0) return RejectUnknown(parsed);
        if (parsed.Positional is null) return RejectMissing("space rotate-secret", "<space id>");

        var result = service.RotateEnrollmentSecret(parsed.Positional);
        if (!result.Success) return Fail(result.Message);

        var credentials = result.Value!;
        if (parsed.Json)
        {
            WriteJson(new { credentials.SpaceId, credentials.EnrollmentSecret });
            return 0;
        }

        Console.WriteLine("Enrollment secret replaced. The new value is shown once - store it safely.");
        Console.WriteLine();
        Console.WriteLine($"  space id           : {credentials.SpaceId}");
        Console.WriteLine($"  enrollment secret  : {credentials.EnrollmentSecret}");
        Console.WriteLine();
        Console.WriteLine("The previous secret no longer enrolls new devices. Devices already paired are");
        Console.WriteLine("unaffected: they authenticate with their own tokens. Revoke one from the device");
        Console.WriteLine("centre in Novara if you need it gone.");
        return 0;
    }



    private sealed record Parsed(string? Positional, bool Json, bool Yes, string? Name, IReadOnlyList<string> Unknown);







    private static Parsed Parse(string[] args)
    {
        string? positional = null;
        string? name = null;
        var json = false;
        var yes = false;
        var unknown = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var lower = args[i].ToLowerInvariant();
            if (lower == "--json")
                json = true;
            else if (lower == "--yes")
                yes = true;
            else if (lower == "--name" && i + 1 < args.Length)
                name = args[++i];
            else if (args[i].StartsWith("--", StringComparison.Ordinal) || positional is not null)
                unknown.Add(args[i]);
            else
                positional = args[i];
        }

        return new Parsed(positional, json, yes, name, unknown);
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"unknown command: space {command}");
        PrintUsage(Console.Error);
        return 2;
    }

    private static int RejectUnknown(Parsed parsed)
    {
        Console.Error.WriteLine($"unknown or incomplete option: {string.Join(" ", parsed.Unknown)}");
        PrintUsage(Console.Error);
        return 2;
    }

    private static int RejectExtra(string value, string command)
    {
        Console.Error.WriteLine($"{command}: unexpected argument '{value}'");
        PrintUsage(Console.Error);
        return 2;
    }

    private static int RejectMissing(string command, string placeholder)
    {
        Console.Error.WriteLine($"usage: NovaraSync {command} {placeholder}");
        return 2;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"failed: {message}");
        return 1;
    }














    private static void UseUtf8Output()
    {
        try { Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false); }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException or System.Security.SecurityException)
        {


        }
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("usage: NovaraSync space <command> [options]");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine("  create [--name <name>]       Create a space; prints its one-time credentials.");
        writer.WriteLine("  list                         List every space on this server.");
        writer.WriteLine("  show <space id>              Show one space in detail.");
        writer.WriteLine("  delete <space id> --yes      Delete a space and all of its data. Irreversible.");
        writer.WriteLine("  rotate-secret <space id>     Replace the enrollment secret (paired devices keep working).");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --json                       Machine-readable output. Works with every command.");
        writer.WriteLine();
        writer.WriteLine("Environment:");
        writer.WriteLine("  NOVARA_SYNC_DATA             Data directory (default: ./data).");
        writer.WriteLine("  NOVARA_SYNC_STORE            sqlite (default) or file.");
    }

    private static void WriteJson<T>(T value)
        => Console.WriteLine(JsonSerializer.Serialize(value, JsonOut));

    private static string DescribeDevices(SpaceSummary space)
    {
        var text = space.DeviceCount.ToString(CultureInfo.InvariantCulture);
        if (space.RevokedDeviceCount > 0) text += $" ({space.RevokedDeviceCount} revoked)";
        return text;
    }


    private static string FormatTime(DateTime value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";

        string[] units = { "KiB", "MiB", "GiB", "TiB" };
        double value = bytes / 1024.0;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }





    private static void PrintTable(IReadOnlyList<string[]> rows, string indent = "  ", int columnGap = 2)
    {
        var columns = rows.Max(r => r.Length);
        var widths = new int[columns];
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
                widths[c] = Math.Max(widths[c], DisplayWidth(row[c]));
        }

        foreach (var row in rows)
        {
            var line = new StringBuilder(indent);
            for (var c = 0; c < row.Length; c++)
            {
                line.Append(row[c]);
                if (c == row.Length - 1) break;
                line.Append(' ', widths[c] - DisplayWidth(row[c]) + columnGap);
            }
            Console.WriteLine(line.ToString().TrimEnd());
        }
    }






    private static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var ch in text)
        {
            var wide = ch >= 0x1100 && (ch <= 0x115F
                || (ch >= 0x2E80 && ch <= 0xA4CF)
                || (ch >= 0xAC00 && ch <= 0xD7A3)
                || (ch >= 0xF900 && ch <= 0xFAFF)
                || (ch >= 0xFE30 && ch <= 0xFE6F)
                || (ch >= 0xFF00 && ch <= 0xFF60)
                || (ch >= 0xFFE0 && ch <= 0xFFE6));
            width += wide ? 2 : 1;
        }
        return width;
    }
}
