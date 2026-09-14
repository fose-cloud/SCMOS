using System.Text.RegularExpressions;

namespace Scmos.Api.Ai.Operations;

public sealed record ParsedOperationsChange(string Key, Dictionary<string, string> Changes, string Reason);

/// <summary>Explicit chat grammar, not model inference. Ambiguous or unsupported instructions fail closed.</summary>
public static class OperationsChangeCommand
{
    public const string Example = "เสนอแก้งาน KEY; วันที่ 15/09/2026; เวลา 09:30; เหตุผล ลูกค้าขอเลื่อน";
    public static ParsedOperationsChange? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) return null;
        var parts = text.Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length is < 3 or > 6) return null;
        var target = Regex.Match(parts[0], @"^(?:เสนอแก้งาน|/แก้งาน)\s+([A-Za-z0-9_-]{1,80})$", RegexOptions.CultureInvariant);
        if (!target.Success) return null;
        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        string? reason = null;
        foreach (var part in parts.Skip(1))
        {
            var match = Regex.Match(part, @"^(วันที่|เวลา|สถานะ|ผู้รับผิดชอบ|เหตุผล)\s+(.+)$", RegexOptions.CultureInvariant);
            if (!match.Success) return null;
            var value = match.Groups[2].Value.Trim();
            if (value.Length == 0 || value.Any(char.IsControl)) return null;
            var field = match.Groups[1].Value switch
            {
                "วันที่" => "date", "เวลา" => "planTime", "สถานะ" => "status", "ผู้รับผิดชอบ" => "opId", _ => "reason",
            };
            if (field == "reason") { if (reason is not null || value.Length > 400) return null; reason = value; }
            else if (!changes.TryAdd(field, value)) return null;
        }
        return reason is not null && changes.Count > 0 ? new(target.Groups[1].Value, changes, reason) : null;
    }
}
