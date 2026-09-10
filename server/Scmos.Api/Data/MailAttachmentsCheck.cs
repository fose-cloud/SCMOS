using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// What SCMOS does with a file that arrived on an email, with
/// <c>--check-attachments</c>.
///
/// <para>
/// The decisions, not the download. Whether a thing has bytes at all, whether
/// it is small enough to keep, where it is filed and what is said about it when
/// it is refused — every one of those is answered before Graph is called, which
/// is what makes them checkable without a mailbox. The call itself is the thin
/// half, in <c>GraphMailReader</c>, and it cannot be checked here.
/// </para>
/// </summary>
public static class MailAttachmentsCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-attachments")) return null;

        var failed = 0;

        void Check(bool ok, string why)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        Console.WriteLine();
        Console.WriteLine("Which of the three things Graph calls an attachment this is.");
        Console.WriteLine();

        Check(MailAttachments.KindOf("#microsoft.graph.fileAttachment") == MailAttachments.Kind.File,
            "a file attachment is a file");
        Check(MailAttachments.KindOf("#microsoft.graph.itemAttachment") == MailAttachments.Kind.Item,
            "an attached Outlook item is not");
        Check(MailAttachments.KindOf("#microsoft.graph.referenceAttachment") == MailAttachments.Kind.Reference,
            "and a OneDrive link is not either");

        // The suffix, not the whole string: a national cloud or a later version
        // writing the prefix differently must still be recognised rather than
        // silently becoming Unknown.
        Check(MailAttachments.KindOf("microsoft.graph.FILEATTACHMENT") == MailAttachments.Kind.File,
            "the type is read however it is cased and prefixed");
        Check(MailAttachments.KindOf(null) == MailAttachments.Kind.Unknown, "nothing at all is unknown");
        Check(MailAttachments.KindOf("   ") == MailAttachments.Kind.Unknown, "and so is blank");
        Check(MailAttachments.KindOf("#microsoft.graph.somethingNew") == MailAttachments.Kind.Unknown,
            "and so is a type nobody here has heard of");

        Console.WriteLine();
        Console.WriteLine("Whether the bytes are worth asking for.");
        Console.WriteLine();

        var ordinary = MailAttachments.Wanted(MailAttachments.Kind.File, 240_000);
        Check(ordinary.Fetch, "an ordinary attached file is fetched");
        Check(ordinary.Why.Length == 0, "and nothing is written on the row about it");

        var link = MailAttachments.Wanted(MailAttachments.Kind.Reference, 1);
        Check(!link.Fetch, "a OneDrive link is not fetched");
        // The reason is shown on the message, so it has to be a sentence a
        // person reads rather than a code.
        Check(link.Why.Length > 0, "and the message says why, in words");

        Check(!MailAttachments.Wanted(MailAttachments.Kind.Item, 5_000).Fetch,
            "an email attached to an email is not fetched as a file");

        Check(!MailAttachments.Wanted(MailAttachments.Kind.File, 0).Fetch,
            "an attachment Graph reports as empty is not fetched");
        Check(!MailAttachments.Wanted(MailAttachments.Kind.File, -1).Fetch,
            "nor one whose size came back negative");

        Check(MailAttachments.Wanted(MailAttachments.Kind.File, MailAttachments.MaxBytes).Fetch,
            "a file exactly at the limit is fetched");
        Check(!MailAttachments.Wanted(MailAttachments.Kind.File, MailAttachments.MaxBytes + 1).Fetch,
            "and one byte over it is not");

        /*
         * The deliberate one. Unknown is fetched, not refused.
         *
         * If @odata.type ever stopped arriving, refusing Unknown would mean the
         * integration stored nothing at all while looking like it worked.
         * Trying costs at most three calls per odd attachment and the ladder
         * stops there. Stated as a check so nobody "fixes" it later by making
         * the safe-looking change.
         */
        Check(MailAttachments.Wanted(MailAttachments.Kind.Unknown, 1_000).Fetch,
            "a kind nobody recognises is tried anyway, because the other mistake is silent");

        Console.WriteLine();
        Console.WriteLine("And the limit is the one a person already lives with.");
        Console.WriteLine();

        // Two numbers here would mean the same file is refused when it arrives
        // by mail and accepted when somebody drags it onto a job.
        Check(MailAttachments.MaxBytes == DocumentService.MaxBytes,
            $"the mail limit and the upload limit are the same {MailAttachments.MaxBytes / (1024 * 1024)} MB");

        Console.WriteLine();
        Console.WriteLine("What happens when a download fails.");
        Console.WriteLine();

        // Deferred to the message queue rather than restated. Checked as an
        // identity over every code, so a second ladder cannot quietly appear.
        var codes = new[]
        {
            GraphDiagnosis.Code.NoConsent, GraphDiagnosis.Code.NotScoped, GraphDiagnosis.Code.Throttled,
            GraphDiagnosis.Code.NotFound, GraphDiagnosis.Code.GraphError, GraphDiagnosis.Code.Unexpected,
            GraphDiagnosis.Code.Rejected, GraphDiagnosis.Code.NoToken, GraphDiagnosis.Code.NotApproved,
        };
        var agree = codes.All(code => Enumerable.Range(0, MailAttachments.MaxAttempts + 1)
            .All(spent => MailAttachments.Decide(code, false, spent) == MailQueue.Decide(code, false, spent)));
        Check(agree, "an attachment takes the message queue's ladder, code for code and attempt for attempt");

        Check(MailAttachments.MaxAttempts == MailQueue.MaxRetries,
            "and gives up after the same number of attempts");

        Console.WriteLine();
        Console.WriteLine("Where the file is filed.");
        Console.WriteLine();

        var arrived = new DateTimeOffset(2026, 9, 10, 3, 15, 0, TimeSpan.FromHours(7));
        // The message's own arrival, not the clock: a file pulled on a retry in
        // January belongs with the mail it came on.
        Check(MailAttachments.Year(arrived) == "2026", "the year is the message's, not today's");
        Check(MailAttachments.Period(arrived) == "2026-09", "and so is the month folder");

        // 03:15 at +07:00 is the previous day in UTC, and the folder follows
        // UTC because every other stamp in this system does.
        var midnight = new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.FromHours(7));
        Check(MailAttachments.Period(midnight) == "2026-09",
            "an early-morning Bangkok arrival files under the UTC month, like every other stamp here");

        Check(MailAttachments.Year(default) == DateTimeOffset.UtcNow.Year.ToString("D4"),
            "a message with no arrival time is filed under now, not under the year 1");

        var key = BlobPaths.ForMail("2026", "2026-09", "packing list.pdf");
        Check(key.StartsWith("SCMOS/Mail/2026/2026-09/", StringComparison.Ordinal),
            $"the key is SCMOS/Mail/{{year}}/{{period}}/{{file}} — {key}");
        Check(key.EndsWith(".pdf", StringComparison.Ordinal), "and it keeps the extension");

        // The lesson from the job tree: a wholly Thai name cleans away to
        // nothing and a naive trim takes the extension with it.
        var thai = BlobPaths.ForMail("2026", "2026-09", "ใบส่งของ ลูกค้า.pdf");
        Check(thai.EndsWith(".pdf", StringComparison.Ordinal),
            $"a wholly Thai filename still ends in a usable extension — {thai}");

        // A slash in a filename would silently invent a folder level.
        var sneaky = BlobPaths.ForMail("2026", "../../etc", "a/b/passwd");
        Check(sneaky.StartsWith("SCMOS/Mail/2026/etc/", StringComparison.Ordinal),
            $"a period that tries to climb the tree is flattened — {sneaky}");
        Check(sneaky.Count(c => c == '/') == 4,
            "and a filename with slashes in it does not invent folders");

        Console.WriteLine();
        Console.WriteLine("What the document register is told.");
        Console.WriteLine();

        Check(MailAttachments.Scope == "mail",
            "a mail attachment is its own scope, beside job, supplier and driver");
        Check(MailAttachments.FiledBy.Length > 0 && !MailAttachments.FiledBy.Contains('@'),
            "and is filed by the integration rather than by the person who sent it");

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "All mail attachment checks passed."
            : $"{failed} mail attachment check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
