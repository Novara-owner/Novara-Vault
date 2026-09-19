using System.Collections.Generic;
using System.Linq;

namespace Novara.Services;

public static class MemoEntryTypes
{

    public static readonly IReadOnlyList<string> All = new[]
    { "邮箱", "账户", "API Key", "网站", "银行卡", "WiFi", "证件", "自定义" };


    public const string Custom = "自定义";



    public static readonly IReadOnlyDictionary<string, string[]> FieldLabels = new Dictionary<string, string[]>
    {
        ["邮箱"] = new[] { "邮箱地址", "邮箱密码", "备注" },
        ["账户"] = new[] { "账号", "密码", "网址", "备注" },
        ["API Key"] = new[] { "API Key", "URL", "模型 ID", "备注" },
        ["网站"] = new[] { "网址", "账号", "密码", "备注" },
        ["银行卡"] = new[] { "卡号", "持卡人", "有效期", "CVV", "密码", "备注" },
        ["WiFi"] = new[] { "网络名", "密码", "备注" },
        ["证件"] = new[] { "证件号", "姓名", "签发机构", "有效期", "备注" },
        [Custom] = System.Array.Empty<string>(),
    };


    public static bool IsKnown(string? type) => !string.IsNullOrEmpty(type) && All.Contains(type);


    public static string[] FieldLabelsFor(string? type)
        => !string.IsNullOrEmpty(type) && FieldLabels.TryGetValue(type, out var labels) ? labels : FieldLabels[Custom];
}
