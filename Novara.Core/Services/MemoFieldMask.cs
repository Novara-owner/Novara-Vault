namespace Novara.Services;

public static class MemoFieldMask
{

    public const string Mask = "••••••••";













    private static readonly HashSet<string> MaskedLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "密码", "邮箱密码", "CVV", "API Key", "卡号", "证件号",
        "password", "passwd", "apikey", "api_key", "api key", "密钥"
    };




    public static bool IsMaskedLabel(string? label)
        => !string.IsNullOrWhiteSpace(label) && MaskedLabels.Contains(label.Trim());






    public static IReadOnlyCollection<string> MaskedLabelNames => MaskedLabels;







    public static string KeyInfoLabelFor(string? type) => type switch
    {
        "邮箱" => "邮箱地址", "账户" => "账号", "网站" => "网址", "银行卡" => "卡号",
        "WiFi" => "网络名", "证件" => "证件号", "API Key" => "URL", _ => "信息"
    };


    public static bool IsMaskedKeyInfo(string? type) => IsMaskedLabel(KeyInfoLabelFor(type));








    public static string MaskedKeyInfo(string? type, string? keyInfo,
        IEnumerable<(string Label, string Value)>? fields = null)
    {
        if (!string.IsNullOrWhiteSpace(keyInfo)) return IsMaskedKeyInfo(type) ? Mask : keyInfo;
        if (fields != null)
            foreach (var (label, value) in fields)
                if (!string.IsNullOrWhiteSpace(value)) return IsMaskedLabel(label) ? Mask : value;
        return "";
    }
}
