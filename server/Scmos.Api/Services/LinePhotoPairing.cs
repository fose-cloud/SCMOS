using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// A driver's photos and the text that follows them, read as one report.
///
/// <para>
/// "ลอรีอัล รถถึงคลังแล้วนะครับ 13.39 น." a minute after two photos — the box
/// door and the seal — is how a driver reports an arrival (17 Sep 2026). The
/// text has the status and the clock and no box; the photo has the box and
/// nothing else. Paired, they are "EOLU7180538 ถึงคลังแล้ว 13:39", which the
/// rest of the integration already knows what to do with. Unpaired, the text
/// waited unanswered and the photo was thrown away.
/// </para>
///
/// <para>
/// A photo is read only for such a text — never on arrival, never for its
/// own sake: the model is paid for once, when a text needs it. The photos
/// that count are the room's, within <see cref="Window"/> before the text,
/// and the same sender's when the sender is known.
/// </para>
/// </summary>
public static class LinePhotoPairing
{
    /// <summary>How long before a text its photos may have been posted.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>The rule on a photo row once its reading has been used by a text.</summary>
    public const string Paired = "photo-paired";

    /// <summary>The rule on a photo row nobody's text has claimed.</summary>
    public const string Waiting = "photo-waiting";

    /// <summary>
    /// Whether a text is the kind a photo completes: a report — a status, or
    /// an arrival clock — naming no job by number, booking, container or seal.
    /// A plate alone still wants the photo: it finds the truck, not the box.
    /// </summary>
    public static bool Wants(LineParser.Parsed read) =>
        !read.Question
        && read.JobNumber is null && read.Container is null && read.SealNumber is null
        && (read.References is null || read.References.Count == 0)
        && (read.Status is not null || read.ArrivalTime is not null);

    /// <summary>
    /// The photos a text may take its box from: the same room, posted within
    /// the window before it (or a moment after — a driver types while the
    /// upload finishes), the same sender when both are known. Pure, for the
    /// check.
    /// </summary>
    public static IReadOnlyList<T> Pick<T>(IEnumerable<T> photos, Func<T, DateTimeOffset> at, Func<T, string> user,
        DateTimeOffset textAt, string textUser) =>
        [.. photos
            .Where(one => at(one) >= textAt - Window && at(one) <= textAt + TimeSpan.FromMinutes(2))
            .Where(one => textUser.Length == 0 || user(one).Length == 0 || user(one) == textUser)
            .OrderBy(at)];

    /// <summary>
    /// The one box the photos around a text show, or null: none read, or two
    /// different ones — a driver posting two trucks' doors is two reports,
    /// and a guess between them is worse than the queue. Readings are made
    /// once and kept on the photo's row; a photo already claimed by another
    /// text is not offered again.
    /// </summary>
    public static async Task<string?> BoxForAsync(ScmosDbContext db, ILineImageReader reader, LineEvent text,
        ILogger log, CancellationToken token)
    {
        var from = text.ReceivedAt - Window;
        var to = text.ReceivedAt + TimeSpan.FromMinutes(2);
        var photos = await db.LineEvents
            .Where(one => one.LineGroupId == text.LineGroupId && one.MessageType == "image"
                && one.ReceivedAt >= from && one.ReceivedAt <= to && one.ErrorCode != Paired)
            .ToListAsync(token);
        var mine = Pick(photos, one => one.ReceivedAt, one => one.LineUserId, text.ReceivedAt, text.LineUserId);
        if (mine.Count == 0) return null;

        var boxes = new List<string>();
        foreach (var photo in mine)
        {
            if (photo.ImageReading.Length == 0 && photo.ImageNote.Length == 0)
            {
                var result = await reader.ReadAsync(photo, token);
                photo.ImageReading = string.Join(", ", result.Reading.Valid);
                photo.ImageNote = result.Failure.Length > 0 ? result.Failure
                    : result.Reading.Rejected.Count > 0
                        ? $"{result.Reading.Note} — อ่านได้แต่ check digit ไม่ผ่าน: {string.Join(", ", result.Reading.Rejected)}".Trim(' ', '—')
                        : result.Reading.Note;
                if (photo.ImageNote.Length > 500) photo.ImageNote = photo.ImageNote[..500];
                log.LogInformation("LINE photo {Id} read for text {Text}: [{Boxes}] {Note}", photo.Id, text.Id, photo.ImageReading, photo.ImageNote);
            }
            foreach (var box in photo.ImageReading.Split(", ", StringSplitOptions.RemoveEmptyEntries))
                if (!boxes.Contains(box, StringComparer.Ordinal)) boxes.Add(box);
        }
        await db.SaveChangesAsync(token);

        if (boxes.Count != 1) return null;
        foreach (var photo in mine.Where(one => one.ImageReading.Contains(boxes[0], StringComparison.Ordinal)))
        {
            photo.ProcessingStatus = LineProcessing.Ignored;
            photo.ErrorCode = Paired;
            photo.ErrorMessage = $"จับคู่กับข้อความ #{text.Id}";
            photo.JobKey = text.JobKey;
        }
        await db.SaveChangesAsync(token);
        return boxes[0];
    }

    /// <summary>
    /// The text's reading with the box its photos showed, when the row
    /// carries one — the same reading the worker made, for the review and
    /// the approval, which parse the text again.
    /// </summary>
    public static LineParser.Parsed WithPhoto(LineParser.Parsed read, LineEvent row)
    {
        // The model's details, when the worker read the text with its help,
        // come back the same way — see LineParser.WithStoredDetails.
        read = LineParser.WithStoredDetails(read, row.MatchedRules);
        if (row.MessageType != "text" || row.ImageReading.Length == 0 || read.Container is not null) return read;
        return read with
        {
            Container = row.ImageReading,
            MatchedRules = [.. read.MatchedRules, $"container-from-photo:{row.ImageReading}"],
            Warnings = [.. read.Warnings.Where(one => one != "no-reference")],
        };
    }
}
