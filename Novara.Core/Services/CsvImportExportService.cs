
using System.Text;
using Novara.Models;

namespace Novara.Services;

public class CsvImportResult
{
    public bool IsNovaraFormat { get; set; }
    public List<MemoEntry> Entries { get; set; } = new();
    public List<string?> GroupNames { get; set; } = new();
    public int SkippedCount { get; set; }
}

public static class CsvImportExportService
{
    private const string CsvHeader = "Group,Type,Name,URL,Username,Password,Notes";

    // Per-type field label mapping for export: (urlLabel, userLabel, passLabel, requiredLabel)
    private static readonly Dictionary<string, (string url, string user, string pass, string required)> TypeMapping = new()
    {
        ["邮箱"] = ("", "邮箱地址", "邮箱密码", "邮箱地址"),
        ["账户"] = ("网址", "账号", "密码", "账号"),
        ["API Key"] = ("URL", "", "API Key", "API Key"),
        ["网站"] = ("网址", "账号", "密码", "网址"),
        ["银行卡"] = ("", "卡号", "密码", "卡号"),
        ["WiFi"] = ("", "网络名", "密码", "网络名"),
        ["证件"] = ("", "证件号", "", "证件号"),
    };

    // Per-type field template for import (field labels in order)
    private static readonly Dictionary<string, string[]> TypeFieldTemplates = new()
    {
        ["邮箱"] = new[] { "邮箱地址", "邮箱密码", "备注" },
        ["账户"] = new[] { "账号", "密码", "网址", "备注" },
        ["API Key"] = new[] { "API Key", "URL", "模型 ID", "备注" },
        ["网站"] = new[] { "网址", "账号", "密码", "备注" },
        ["银行卡"] = new[] { "卡号", "持卡人", "有效期", "CVV", "密码", "备注" },
        ["WiFi"] = new[] { "网络名", "密码", "备注" },
        ["证件"] = new[] { "证件号", "姓名", "签发机构", "有效期", "备注" },
        ["自定义"] = Array.Empty<string>(),
    };

    
    private static readonly Dictionary<string, string> KeyInfoLabel = new()
    {
        ["邮箱"] = "邮箱地址",
        ["账户"] = "账号",
        ["API Key"] = "URL",
        ["网站"] = "网址",
        ["银行卡"] = "卡号",
        ["WiFi"] = "网络名",
        ["证件"] = "证件号",
    };

    private static string GetFieldByLabel(List<EntryField> fields, string label)
    {
        if (string.IsNullOrEmpty(label)) return "";
        return fields.FirstOrDefault(f => f.Label == label)?.Value ?? "";
    }

    private static bool HasFieldValue(List<EntryField> fields, string label)
    {
        return !string.IsNullOrEmpty(GetFieldByLabel(fields, label));
    }

    // ==================== Export ====================

    public static string ExportMemoEntriesToCsv(List<MemoEntry> entries, List<MemoGroup> groups)
    {
        var groupMap = groups.ToDictionary(g => g.Id, g => g.Name);
        var sb = new StringBuilder();
        sb.AppendLine(CsvHeader);

        foreach (var entry in entries)
        {
            if (entry.IsDeleted) continue;
            if (!HasRequiredField(entry)) continue;

            var group = entry.GroupId.HasValue && groupMap.TryGetValue(entry.GroupId.Value, out var gname) ? gname : "";
            var (urlLabel, userLabel, passLabel, _) = TypeMapping.GetValueOrDefault(entry.Type, ("", "", "", ""));

            var url = GetFieldByLabel(entry.Fields, urlLabel);
            var user = string.IsNullOrEmpty(userLabel) ? entry.Name : GetFieldByLabel(entry.Fields, userLabel);
            var pass = GetFieldByLabel(entry.Fields, passLabel);

            var usedLabels = new HashSet<string> { urlLabel, userLabel, passLabel };
            var remainingFields = entry.Fields
                .Where(f => !string.IsNullOrEmpty(f.Label) && !usedLabels.Contains(f.Label) && !string.IsNullOrEmpty(f.Value))
                .Select(f => $"{f.Label}: {f.Value}");
            
            var notes = string.Join("\n", remainingFields);

            sb.AppendLine(string.Join(",",
                CsvEscape(group),
                CsvEscape(entry.Type),
                CsvEscape(entry.Name),
                CsvEscape(url),
                CsvEscape(user),
                CsvEscape(pass),
                CsvEscape(notes)));
        }

        return sb.ToString();
    }

    private static bool HasRequiredField(MemoEntry entry)
    {
        var (_, _, _, required) = TypeMapping.GetValueOrDefault(entry.Type, ("", "", "", ""));
        if (!string.IsNullOrEmpty(required))
            return HasFieldValue(entry.Fields, required);
        return !string.IsNullOrEmpty(entry.Name);
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // NC7 (OWASP CSV injection): a leading =+-@ would execute as a formula when the export is
        // opened in Excel/WPS. Prefix a single quote - spreadsheets treat it as text and hide it.
        if (value[0] is '=' or '+' or '-' or '@')
            value = "'" + value;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    // N2C-2: undo the formula-injection guard on import - a leading ' added before =+-@ by
    // CsvEscape must be stripped back, otherwise the value round-trips polluted.
    private static string UnquoteFormulaPrefix(string value)
    {
        if (value.Length >= 2 && value[0] == '\'' && (value[1] is '=' or '+' or '-' or '@'))
            return value[1..];
        return value;
    }

    // N2C-5: first non-empty (null OR empty) wins - "Password" column present but blank must
    // still fall back to "Login Password" (empty string is not null, so ?? alone would not fall back).
    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrEmpty(v)) return v;
        return "";
    }

    // ==================== Import ====================

    public static CsvImportResult ParseCsv(string csvText, string targetType)
    {
        var result = new CsvImportResult();
        var lines = SplitCsvLines(csvText);
        if (lines.Count < 2) return result;

        var header = ParseCsvLine(lines[0]);
        
        result.IsNovaraFormat = header.Any(h => string.Equals(h.Trim(), "Group", StringComparison.OrdinalIgnoreCase))
                             && header.Any(h => string.Equals(h.Trim(), "Type", StringComparison.OrdinalIgnoreCase));

        for (int i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var values = ParseCsvLine(lines[i]);
            if (values.Count == 0) continue;

            var entry = BuildEntryFromCsvRow(header, values, result.IsNovaraFormat ? null : targetType, out var groupName);
            if (entry == null) { result.SkippedCount++; continue; }
            result.Entries.Add(entry);
            result.GroupNames.Add(groupName);
        }

        return result;
    }

    private static MemoEntry? BuildEntryFromCsvRow(List<string> header, List<string> values, string? forcedType, out string? groupName)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count && i < values.Count; i++)
            row[header[i].Trim()] = values[i];

        var group = row.GetValueOrDefault("Group");
        groupName = string.IsNullOrWhiteSpace(group) ? null : group.Trim();

        var type = forcedType ?? row.GetValueOrDefault("Type") ?? "";
        var name = row.GetValueOrDefault("Name") ?? "";
        var url = row.GetValueOrDefault("URL") ?? "";
        var username = row.GetValueOrDefault("Username") ?? row.GetValueOrDefault("Login Name") ?? "";
        var password = FirstNonEmpty(row.GetValueOrDefault("Password"), row.GetValueOrDefault("Login Password"));
        var notes = row.GetValueOrDefault("Notes") ?? row.GetValueOrDefault("Comments") ?? "";

        
        if (row.ContainsKey("Login Name"))
        {
            username = row["Login Name"];
            // NC2: KeePass variants export "Login Password" as the password column - fall back to it
            password = FirstNonEmpty(row.GetValueOrDefault("Password"), row.GetValueOrDefault("Login Password"));
            url = row.GetValueOrDefault("Web Site") ?? "";
            notes = row.GetValueOrDefault("Comments") ?? "";
            if (string.IsNullOrEmpty(name)) name = row.GetValueOrDefault("Account") ?? username;
        }
        // Bitwarden mapping
        if (row.ContainsKey("login_username"))
        {
            username = row["login_username"];
            password = row.GetValueOrDefault("login_password") ?? "";
            url = row.GetValueOrDefault("login_uri") ?? "";
            notes = row.GetValueOrDefault("notes") ?? "";
            if (string.IsNullOrEmpty(name)) name = row.GetValueOrDefault("name") ?? username;
            type = forcedType ?? row.GetValueOrDefault("type") ?? "";
        }

        if (string.IsNullOrEmpty(name)) name = username;
        if (string.IsNullOrEmpty(name)) return null;

        
        // to create zero-field entries whose notes only surfaced as subtitle text.
        if (!TypeFieldTemplates.ContainsKey(type))
            type = forcedType ?? "自定义";

        var entry = new MemoEntry
        {
            GroupId = null,
            Type = string.IsNullOrEmpty(type) ? "自定义" : type,
            Name = name,
            KeyInfo = "",
            CreatedAt = DateTime.Now,
        };

        BuildFieldsForEntry(entry, type, url, username, password, notes);
        if (!HasRequiredField(entry)) return null;

        return entry;
    }

    private static void BuildFieldsForEntry(MemoEntry entry, string type, string url, string username, string password, string notes)
    {
        if (!TypeFieldTemplates.TryGetValue(type, out var template))
        {
            entry.KeyInfo = notes;
            return;
        }

        if (template.Length == 0) 
        {
            var infoValues = ParseCustomInfoValues(notes);
            entry.Fields = infoValues.Select(v => new EntryField { Label = "信息", Value = v, CanCopy = true }).ToList();
            entry.KeyInfo = infoValues.Count > 0 ? infoValues[0] : "";
            return;
        }

        entry.Fields = template.Select(label => new EntryField
        {
            Label = label,
            Value = MatchFieldValue(label, url, username, password),
            CanCopy = IsCopyableField(label, type),
        }).ToList();

        
        var notesFields = ParseNotesToFields(notes, template);
        foreach (var f in entry.Fields)
        {
            if (!notesFields.TryGetValue(f.Label, out var v) || string.IsNullOrEmpty(v)) continue;
            // N4A-03: the dedicated CSV column is authoritative - a note line repeating a known label
            
            // overwrite what the column carried.
            if (string.IsNullOrEmpty(f.Value)) f.Value = v;
        }

        entry.KeyInfo = KeyInfoLabel.TryGetValue(type, out var kiLabel)
            ? (entry.Fields.FirstOrDefault(f => f.Label == kiLabel)?.Value ?? "")
            : "";
    }

    private static Dictionary<string, string> ParseNotesToFields(string notes, IEnumerable<string> knownLabels)
    {
        var map = new Dictionary<string, string>();
        var labelSet = new HashSet<string>(knownLabels, StringComparer.Ordinal);
        if (string.IsNullOrEmpty(notes)) return map;
        foreach (var rawLine in notes.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var idx = line.IndexOf(':');
            // N2C-3: only treat the line as a new "label: value" when the label is a KNOWN field label;
            // otherwise a continuation line that happens to contain ':' would be misparsed as a new field.
            if (idx > 0 && idx < line.Length - 1)
            {
                var label = line[..idx].Trim();
                if (labelSet.Contains(label))
                {
                    var value = line[(idx + 1)..].Trim();
                    if (!string.IsNullOrEmpty(value)) map[label] = value;
                    continue;
                }
            }
            if (map.Count > 0)
            {
                // NC4: continuation line of a multi-line value - append to the previous entry
                // instead of dropping it (export encodes embedded newlines as bare lines).
                var lastKey = map.Keys.Last();
                map[lastKey] += "\n" + line;
            }
        }
        return map;
    }

    private static List<string> ParseCustomInfoValues(string notes)
    {
        var values = new List<string>();
        if (string.IsNullOrEmpty(notes)) return values;
        foreach (var rawLine in notes.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var idx = line.IndexOf(':');
            var value = idx > 0 && idx < line.Length - 1 ? line[(idx + 1)..].Trim() : line;
            if (!string.IsNullOrEmpty(value)) values.Add(value);
        }
        return values;
    }

    private static string MatchFieldValue(string fieldLabel, string url, string username, string password)
    {
        if (fieldLabel.Contains("网址") || fieldLabel.Equals("URL", StringComparison.OrdinalIgnoreCase) || fieldLabel.Contains("URL"))
            return url;
        if (fieldLabel.Contains("账号") || fieldLabel.Contains("地址") || fieldLabel.Contains("卡号") ||
            fieldLabel.Contains("网络名") || fieldLabel.Contains("证件号") || fieldLabel.Contains("姓名") ||
            fieldLabel.Contains("持卡人"))
            return username;
        if (fieldLabel.Contains("密码") || fieldLabel.Contains("Key") || fieldLabel.Contains("CVV"))
            return password;
        return "";
    }

    // NC5: (type,label)-aware copyability mirroring the creation form exactly - label-only matching
    
    private static readonly Dictionary<string, HashSet<string>> CopyableByType = new()
    {
        ["邮箱"]    = new(StringComparer.Ordinal) { "邮箱地址", "邮箱密码" },
        ["账户"]    = new(StringComparer.Ordinal) { "账号", "密码" },
        ["API Key"] = new(StringComparer.Ordinal) { "API Key", "URL", "模型 ID" },
        ["网站"]    = new(StringComparer.Ordinal) { "网址", "账号", "密码" },
        ["银行卡"]  = new(StringComparer.Ordinal) { "卡号", "CVV", "密码" },
        ["WiFi"]    = new(StringComparer.Ordinal) { "网络名", "密码" },
        ["证件"]    = new(StringComparer.Ordinal) { "证件号" },
    };

    private static bool IsCopyableField(string label, string type)
        => type == "自定义" || (CopyableByType.TryGetValue(type, out var set) && set.Contains(label));

    // ==================== CSV Parsing ====================

    private static List<string> SplitCsvLines(string text)
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        foreach (var ch in text)
        {
            if (ch == '"') inQuotes = !inQuotes;
            if (ch == '\n' && !inQuotes)
            {
                lines.Add(sb.ToString().TrimEnd('\r'));
                sb.Clear();
            }
            else if (ch != '\r' || inQuotes)
            {
                sb.Append(ch);
            }
        }
        var last = sb.ToString().TrimEnd('\r');
        if (!string.IsNullOrWhiteSpace(last)) lines.Add(last);

        return lines;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                // NC1: no Trim on data fields - leading/trailing spaces inside quoted values are
                // legitimate password/note content; header cells are trimmed at consumption time.
                fields.Add(UnquoteFormulaPrefix(sb.ToString()));
                sb.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }
        fields.Add(UnquoteFormulaPrefix(sb.ToString()));
        return fields;
    }
}