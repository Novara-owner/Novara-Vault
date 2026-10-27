using System.Globalization;

namespace Novara.Services;

public static class PathPropertyFormatter
{
    private const long Kb = 1024;
    private const long Mb = Kb * 1024;
    private const long Gb = Mb * 1024;
    private const long Tb = Gb * 1024;


    public static string FormatSize(long bytes, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.InvariantCulture;
        return bytes switch
        {
            < Kb => $"{bytes.ToString(provider)} B",
            < Mb => $"{(bytes / Kb).ToString(provider)} KB",
            < Gb => $"{(bytes / (double)Mb).ToString("0.0", provider)} MB",
            < Tb => $"{(bytes / (double)Gb).ToString("0.0", provider)} GB",
            _ => $"{(bytes / (double)Tb).ToString("0.0", provider)} TB",
        };
    }



    public static string FormatCount(long count, IFormatProvider? provider = null)
        => count.ToString("N0", provider ?? CultureInfo.InvariantCulture);
}
