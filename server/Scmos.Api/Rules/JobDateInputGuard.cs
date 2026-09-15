using System.Text.Json;

namespace Scmos.Api.Rules;

/// <summary>Reject newly introduced impossible dates, not unchanged legacy data or WAIT.</summary>
public static class JobDateInputGuard
{
    private static readonly string[] Fields = ["date", "arrDate", "closingDate"];

    /// <summary>The column as the grid heads it, for the refusal an operator reads.</summary>
    public static string Label(string field) => field switch
    {
        "date" => "วันที่งาน (Date)",
        "arrDate" => "วันที่ถึง (Arrival Date)",
        "closingDate" => "วันปิดตู้ (Closing Date)",
        _ => field,
    };

    public static string? InvalidField(JsonElement job, IReadOnlyDictionary<string, string>? before = null)
    {
        if (job.ValueKind != JsonValueKind.Object) return null;
        foreach (var field in Fields)
        {
            if (!job.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String) continue;
            var text = value.GetString();
            if (!Formats.IsImpossibleDate(text)) continue;
            if (before is not null && before.TryGetValue(field, out var old)
                && string.Equals(text, old, StringComparison.Ordinal)) continue;
            return field;
        }
        return null;
    }
}
