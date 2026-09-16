using System.Text.Json;

namespace Scmos.Api.Rules;

/// <summary>
/// What a model's reading of a photograph becomes before SCMOS trusts any of it.
///
/// <para>
/// The model is asked for the container numbers it can see and nothing else,
/// and answers in a fixed JSON shape. This turns that answer into the numbers
/// that pass <see cref="ContainerNumbers.IsValid"/> and the ones that do not.
/// A number one digit off — the ordinary way a photo is misread, a 5 for a 6
/// through mud or shadow — fails the check digit and is refused here, before
/// it can be offered to anybody. Pure, so <c>--check-line</c> can prove it
/// with a handful of answers and no model.
/// </para>
/// </summary>
public static class LineImageReading
{
    /// <summary>
    /// What the model is told. Read-only, one job, no invention: a photo of a
    /// delivery note or a driver is answered with an empty list and a note.
    /// </summary>
    public const string Instructions =
        "You are reading a photograph posted by a Thai haulier's driver in a LINE group. " +
        "Report every shipping container number that is legibly painted or printed on a container " +
        "in the photo — the owner code of four letters followed by seven digits, such as MSKU1234567. " +
        "Read only what is visible; never guess a digit, and if a number is partly hidden or blurred " +
        "leave it out. If no container number is visible, return an empty list. In the note, say in " +
        "one short sentence what the photo shows.";

    /// <summary>The shape the model must answer in. Strict: nothing else can come back.</summary>
    public const string Schema =
        "{\"type\":\"object\",\"properties\":{" +
        "\"containers\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
        "\"note\":{\"type\":\"string\"}}," +
        "\"required\":[\"containers\",\"note\"],\"additionalProperties\":false}";

    /// <param name="Valid">Numbers that are shaped like a container and whose check digit agrees, in the order read, without repeats.</param>
    /// <param name="Rejected">Numbers the model offered that failed the shape or the check digit — shown to a person, never used.</param>
    /// <param name="Note">The model's one sentence on what the photo shows, or what went wrong reading its answer.</param>
    public record Reading(IReadOnlyList<string> Valid, IReadOnlyList<string> Rejected, string Note)
    {
        public static readonly Reading Empty = new([], [], "");
    }

    /// <summary>Reads the model's JSON answer. Never throws: an unreadable answer is a Reading with a note.</summary>
    public static Reading Read(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return new Reading([], [], "ไม่มีคำตอบจากโมเดล");

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(answer).RootElement;
        }
        catch (JsonException)
        {
            return new Reading([], [], "คำตอบจากโมเดลอ่านไม่ออก");
        }

        var note = root.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String
            ? (n.GetString() ?? "").Trim()
            : "";
        if (note.Length > 200) note = note[..200];

        var valid = new List<string>();
        var rejected = new List<string>();
        if (root.TryGetProperty("containers", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var one in list.EnumerateArray())
            {
                if (one.ValueKind != JsonValueKind.String) continue;
                var number = ContainerNumbers.Normalise(one.GetString());
                if (number.Length == 0) continue;
                if (ContainerNumbers.IsValid(number))
                {
                    if (!valid.Contains(number, StringComparer.Ordinal)) valid.Add(number);
                }
                else if (!rejected.Contains(number, StringComparer.Ordinal))
                {
                    rejected.Add(number);
                }
            }
        }
        return new Reading(valid, rejected, note);
    }
}
