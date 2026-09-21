using System.Text.Json;
using System.Text.RegularExpressions;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai.Communication;

/// <summary>A job as the message evidence names it — enough to know which job, never the driver's details.</summary>
public sealed record MessageJob(string Key, string JobCode, string Customer, string Trucker, string OwnerId, string Category, string Date, string Status);

/// <summary>A LINE row as the source hands it over: the stored row and the name of its room.</summary>
public sealed record LineMessageRow(LineEvent Row, string GroupName);

/// <summary>A mail as the source hands it over: the message and the link that ties it to a job.</summary>
public sealed record MailMessageRow(long EmailId, string Subject, string FromName, DateTimeOffset SentAt, string ProcessingStatus,
    bool HasAttachments, string JobKey, string LinkStatus, double Confidence, string MatchedOn);

/// <summary>Where the messages come from — the tables the LINE and mail screens read, behind a boundary the checks can stand in for.</summary>
public interface ICommunicationSource
{
    /// <summary>Jobs whose key, job code, container or customer matches the text (case-insensitive), newest plan date first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<MessageJob>> FindJobsAsync(string query, int take, CancellationToken token);
    /// <summary>The jobs behind a set of keys, for labelling rows.</summary>
    Task<IReadOnlyList<MessageJob>> JobsAsync(IReadOnlyCollection<string> keys, CancellationToken token);
    /// <summary>LINE and TMS rows received since <paramref name="since"/>, newest first, at most <paramref name="take"/> — pinned to the given jobs when keys are given.</summary>
    Task<IReadOnlyList<LineMessageRow>> LineAsync(DateTimeOffset since, IReadOnlyCollection<string>? jobKeys, int take, CancellationToken token);
    /// <summary>Mails linked to the given jobs, newest first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<MailMessageRow>> MailAsync(IReadOnlyCollection<string> jobKeys, int take, CancellationToken token);
}

/// <summary>
/// One message as the evidence carries it: which channel, when, whose room,
/// which job, what the parser read out of it, what the system did with it,
/// and an excerpt with phone numbers masked. The driver's name and contact
/// the message may carry are not here — the same line the Operations
/// evidence draws.
/// </summary>
public sealed record MessageEvidence(
    string Id, string Channel, DateTimeOffset At, string Group,
    string JobKey, string JobCode, string Customer, string Trucker,
    string? Status, string? Arrival, string? Eta, string? Plate, string? Container, string? Seal,
    bool Delayed, string? DelayCategory, bool Question,
    /// <summary>applied · waiting · unmatched · ignored · failed · pending — for a mail: linked · suggested · rejected.</summary>
    string State, string Detail, string Excerpt, string Source);

public sealed record MessagesAnswer(string View, string AsOfDate, string TimeZone, string Window,
    int Total, int Returned, bool Truncated,
    int Waiting, int Applied, int Unmatched, int Ignored, int Mails,
    IReadOnlyList<MessageJob> Jobs, DateTimeOffset RetrievedAt, string Basis,
    IReadOnlyList<MessageEvidence> Rows);

/// <summary>Reuses the LINE parser's own reading of each row and the mail links as stored. No LLM reads a message; nothing is sent or applied.</summary>
public sealed partial class MessagesReadService(ICommunicationSource? source, TimeProvider clock)
{
    public const string Tool = "query_messages";
    public static readonly string[] Views = ["job", "waiting", "unmatched", "today"];
    public const int EvidenceLimit = 50;
    public const int MaxDays = 30;
    public const int ExcerptLength = 160;

    /// <summary>Whether the tables stand behind this read.</summary>
    public bool Connected => source is not null;

    public async Task<MessagesAnswer> ReadAsync(string tool, JsonElement arguments, AiToolContext context, CancellationToken token)
    {
        if (tool != Tool) throw new InvalidOperationException("Unknown read tool.");
        if (!context.Scope.Team && string.IsNullOrWhiteSpace(context.Scope.OperatorId))
            throw new UnauthorizedAccessException("An explicit operator scope is required.");
        if (source is null) throw new InvalidOperationException("No message source is connected.");
        var view = arguments.GetProperty("view").GetString() ?? "";
        var limit = arguments.GetProperty("limit").GetInt32();
        var days = arguments.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : 7;
        var query = arguments.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String ? Clean(q.GetString(), 120) : "";
        if (!Views.Contains(view, StringComparer.Ordinal) || limit is < 1 or > EvidenceLimit || days is < 1 or > MaxDays)
            throw new InvalidOperationException("Invalid read arguments.");
        if (view == "job" && query.Length == 0) throw new InvalidOperationException("A job view needs a job.");

        var now = context.AsOf ?? clock.GetUtcNow();
        var bangkok = now.ToOffset(Formats.Zone);
        var today = DateOnly.FromDateTime(bangkok.DateTime);
        var restricted = context.Scope.Team ? null : context.Scope.OperatorId;

        // Which jobs the question is about, and which the reader may see.
        IReadOnlyList<MessageJob> jobs = [];
        IReadOnlyCollection<string>? keys = null;
        if (view == "job")
        {
            jobs = (await source.FindJobsAsync(query, 5, token))
                .Where(job => restricted is null || string.Equals(job.OwnerId, restricted, StringComparison.OrdinalIgnoreCase)).ToList();
            keys = jobs.Select(job => job.Key).ToList();
        }
        var since = view switch
        {
            "job" => DateTimeOffset.MinValue,
            "today" => new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), Formats.Zone),
            _ => bangkok.AddDays(-days),
        };
        var window = view switch
        {
            "job" => "all_dates_for_the_job",
            "today" => "received_today",
            _ => $"received_last_{days}_days",
        };

        var lines = keys is { Count: 0 } ? [] : await source.LineAsync(since, keys, 500, token);
        var wanted = new List<MessageEvidence>();
        int waiting = 0, applied = 0, unmatched = 0, ignored = 0;
        var pinned = lines.Select(one => one.Row.JobKey).Where(key => key.Length > 0).Distinct().ToList();
        var labels = (keys is null ? await source.JobsAsync(pinned, token) : jobs).ToDictionary(job => job.Key, StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var row = line.Row;
            var state = StateOf(row);
            switch (state) { case "waiting": waiting++; break; case "applied": applied++; break; case "unmatched": unmatched++; break; case "ignored": ignored++; break; }
            var include = view switch
            {
                "job" => row.JobKey.Length > 0,
                "waiting" => state == "waiting",
                "unmatched" => state == "unmatched",
                "today" => state != "ignored",
                _ => false,
            };
            if (!include) continue;
            // A restricted reader sees messages about their own jobs only; a
            // message pinned to nothing is nobody's, so it is not theirs either.
            labels.TryGetValue(row.JobKey, out var job);
            if (restricted is not null && (job is null || !string.Equals(job.OwnerId, restricted, StringComparison.OrdinalIgnoreCase))) continue;
            wanted.Add(Describe(line, job, state));
        }

        var mails = 0;
        if (view == "job" && keys is { Count: > 0 })
        {
            foreach (var mail in await source.MailAsync(keys, 100, token))
            {
                labels.TryGetValue(mail.JobKey, out var job);
                mails++;
                wanted.Add(new MessageEvidence($"mail:{mail.EmailId}", "mail", mail.SentAt, Clean(mail.FromName, 80),
                    mail.JobKey, job?.JobCode ?? "", job?.Customer ?? "", job?.Trucker ?? "",
                    null, null, null, null, null, null, false, null, false,
                    mail.LinkStatus == MailLink.Confirmed ? "linked" : mail.LinkStatus == MailLink.Rejected ? "rejected" : "suggested",
                    $"จับคู่จาก {mail.MatchedOn} ({Math.Round(mail.Confidence * 100)}%)" + (mail.HasAttachments ? " · มีไฟล์แนบ" : ""),
                    Excerpt(mail.Subject), "emails"));
            }
        }

        wanted.Sort((a, b) => b.At.CompareTo(a.At));
        var total = wanted.Count;
        var rows = wanted.Take(limit).ToList();
        return new MessagesAnswer(view, Formats.PlanDate(today), "Asia/Bangkok", window,
            total, rows.Count, total > rows.Count, waiting, applied, unmatched, ignored, mails,
            jobs, now,
            "SCMOS LineParser / LineAuthority readings and mail links as stored; excerpts with phone numbers masked; driver names and contacts omitted; nothing sent, nothing applied",
            rows);
    }

    /// <summary>What the system did with a row, in one word the reader can group by.</summary>
    public static string StateOf(LineEvent row) => row.ProcessingStatus switch
    {
        LineProcessing.Processed => "applied",
        LineProcessing.NeedReview => row.JobKey.Length > 0 ? "waiting" : "unmatched",
        LineProcessing.Ignored => "ignored",
        LineProcessing.Failed => "failed",
        _ => "pending",
    };

    private static MessageEvidence Describe(LineMessageRow line, MessageJob? job, string state)
    {
        var row = line.Row;
        var read = LineReadings.Of(row);
        var tms = LineReadings.IsTms(row);
        var arrival = read.ArrivalTime is { } at ? at.ToOffset(Formats.Zone).ToString("dd/MM/yyyy HH:mm") : null;
        var eta = read.Eta is { } e ? e.ToOffset(Formats.Zone).ToString("HH:mm") : null;
        var detail = state switch
        {
            "waiting" => "รอเจ้าของงานอนุมัติ" + (row.ErrorCode.Length > 0 ? $" ({row.ErrorCode})" : ""),
            "unmatched" => "จับคู่งานไม่ได้" + (row.ErrorMessage.Length > 0 ? " — " + Clean(row.ErrorMessage, 120) : ""),
            "applied" => "นำเข้าตารางงานแล้ว",
            "ignored" => "ไม่ใช่ข้อความเกี่ยวกับงาน",
            "failed" => "ประมวลผลไม่สำเร็จ",
            _ => "ยังไม่ได้อ่าน",
        };
        return new MessageEvidence($"{(tms ? "tms" : "line")}:{row.Id}", tms ? "tms" : "line", row.ReceivedAt,
            Clean(line.GroupName.Length > 0 ? line.GroupName : (LineReadings.EventOf(row)?.GroupLabel ?? ""), 80),
            row.JobKey, job?.JobCode ?? "", job?.Customer ?? "", job?.Trucker ?? "",
            read.Status, arrival, eta, read.Plate, read.Container, read.SealNumber,
            read.Delayed, read.DelayCategory?.ToString(), read.Question,
            state, detail, Excerpt(tms ? (LineReadings.EventOf(row)?.Text() ?? row.RawText) : row.RawText, read.Driver, read.Phone), "line_events");
    }

    /// <summary>
    /// The message's first line-and-a-bit, plain, with phone numbers masked
    /// and the driver's name the parser read taken out. What a supervisor may
    /// already read in the drawer, minus the person.
    /// </summary>
    public static string Excerpt(string? text, string? driver = null, string? phone = null)
    {
        var plain = Clean(text, 4000);
        if (!string.IsNullOrWhiteSpace(phone)) plain = plain.Replace(phone.Trim(), MaskPhone(phone.Trim()), StringComparison.Ordinal);
        plain = Phone().Replace(plain, match => MaskPhone(match.Value));
        if (!string.IsNullOrWhiteSpace(driver)) plain = plain.Replace(driver.Trim(), "[คนขับ]", StringComparison.OrdinalIgnoreCase);
        return plain.Length > ExcerptLength ? plain[..ExcerptLength] + "…" : plain;
    }

    private static string MaskPhone(string digits) => digits.Length <= 3 ? digits : digits[..3] + new string('x', digits.Length - 3);

    /// <summary>A Thai phone: a leading 0, then eight or nine digits, dashes or spaces allowed between groups.</summary>
    [GeneratedRegex(@"(?<!\d)0\d{1,2}[-\s]?\d{3}[-\s]?\d{3,4}(?!\d)")]
    private static partial Regex Phone();

    private static string Clean(string? text, int max)
    {
        var value = new string((text ?? "").Select(c => c is '\n' or '\r' or '\t' ? ' ' : c).Where(c => !char.IsControl(c)).ToArray());
        while (value.Contains("  ")) value = value.Replace("  ", " ");
        value = value.Trim();
        return value.Length > max ? value[..max] : value;
    }
}

public sealed class MessagesReadHandler(MessagesReadService service) : IAiReadToolHandler
{
    public async Task<JsonElement> ReadAsync(JsonElement arguments, AiToolContext context, CancellationToken token)
        => JsonSerializer.SerializeToElement(await service.ReadAsync(MessagesReadService.Tool, arguments, context, token));
}
