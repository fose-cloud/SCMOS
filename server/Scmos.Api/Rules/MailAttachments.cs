namespace Scmos.Api.Rules;

/// <summary>
/// Which of a message's attachments are worth pulling out of Graph, and what
/// happens to the row when pulling one does not work.
///
/// <para>
/// The list of attachments is read when the message is stored; the bytes are
/// fetched afterwards, on their own pass. That split is deliberate and it is
/// why this file exists: a message must be readable the moment it arrives, and
/// a thirty megabyte packing list must not hold the queue while it downloads.
/// A row whose <c>StoredDocumentId</c> is still zero is the ordinary state
/// between the two, not a fault.
/// </para>
///
/// <para>
/// <b>Not everything attached to a mail is a file.</b> Graph calls three
/// different things an attachment, and only one of them has bytes — see
/// <see cref="Kind"/>. Asking for the content of the other two produces an
/// error that looks like Graph having a bad minute and is not: it will fail the
/// same way for ever. Deciding that here, from what the list already told us,
/// is what keeps those out of the retry budget.
/// </para>
/// </summary>
public static class MailAttachments
{
    /// <summary>
    /// The largest attachment SCMOS will store.
    ///
    /// The same 32 MB a person may upload by hand. Two numbers here would mean
    /// a file that arrives by mail is refused at a size the same file is
    /// accepted at when somebody drags it onto a job, which is the sort of
    /// difference nobody can explain to the person it happens to — the check
    /// asserts the two agree.
    /// </summary>
    public const long MaxBytes = 32L * 1024 * 1024;

    /// <summary>
    /// How many times the bytes are asked for before the row is left alone.
    ///
    /// The same three as the message queue. This is counted per attachment
    /// rather than per message: one unreadable file among four should not stop
    /// the other three being fetched, and should not spend their attempts.
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// What Graph means by "attachment". Only the first has bytes.
    ///
    /// <list type="bullet">
    /// <item><b>file</b> — an actual file. <c>/$value</c> returns it.</item>
    /// <item><b>item</b> — another Outlook item, usually a forwarded message.
    /// It has no bytes of its own; it is a message, and getting it means asking
    /// for a message, which is a different shape and a different job. Recorded
    /// so the screen can say what it is instead of showing a file that never
    /// downloads.</item>
    /// <item><b>reference</b> — a link to OneDrive or SharePoint. The file is
    /// not in the mail at all, and following the link would mean reading a
    /// second service under a permission nobody has granted. Recorded, never
    /// fetched.</item>
    /// </list>
    /// </summary>
    public static class Kind
    {
        public const string File = "file";
        public const string Item = "item";
        public const string Reference = "reference";

        /// <summary>Something Graph has that this does not know about yet.</summary>
        public const string Unknown = "unknown";
    }

    /// <summary>
    /// The kind, from Graph's <c>@odata.type</c>.
    ///
    /// Matched on the suffix rather than the whole string because the type
    /// arrives as <c>#microsoft.graph.fileAttachment</c>, and a national cloud
    /// or a future version writing it differently should still be recognised
    /// rather than silently becoming Unknown and never fetched.
    /// </summary>
    public static string KindOf(string? odataType)
    {
        var text = (odataType ?? "").Trim();
        if (text.Length == 0) return Kind.Unknown;
        if (text.EndsWith("fileAttachment", StringComparison.OrdinalIgnoreCase)) return Kind.File;
        if (text.EndsWith("itemAttachment", StringComparison.OrdinalIgnoreCase)) return Kind.Item;
        if (text.EndsWith("referenceAttachment", StringComparison.OrdinalIgnoreCase)) return Kind.Reference;
        return Kind.Unknown;
    }

    /// <summary>Whether to ask for the bytes, and if not, what to write down instead.</summary>
    /// <param name="Fetch">True to go and get it.</param>
    /// <param name="Why">
    /// Empty when fetching. Otherwise the reason, in Thai, to be shown on the
    /// message rather than logged — somebody looking at a mail with a paperclip
    /// and no file is owed the sentence that says why.
    /// </param>
    public record Verdict(bool Fetch, string Why);

    private static readonly Verdict Go = new(true, "");

    /// <summary>
    /// Whether this attachment's bytes are worth asking Graph for.
    ///
    /// <para>
    /// Everything refused here is refused permanently, from what the attachment
    /// list already said, without a call. That is the point: a reference
    /// attachment and a 60 MB video would both fail on their own eventually,
    /// but they would fail three times each first, and the message would carry
    /// a red error that reads like a fault in SCMOS rather than a description
    /// of what arrived.
    /// </para>
    ///
    /// <para>
    /// A zero-byte attachment is refused too. Graph reports a size of 0 for an
    /// attachment whose content it could not produce, and an empty blob is
    /// worse than no blob: it looks stored.
    /// </para>
    ///
    /// <para>
    /// <b>A kind this does not recognise is fetched anyway</b>, which looks
    /// like the wrong way round until you ask what each mistake costs. The kind
    /// comes from <c>@odata.type</c>, an OData control annotation Graph emits
    /// for every derived type and which no <c>$select</c> suppresses — so
    /// Unknown should never occur. If it ever does, refusing it means the
    /// integration quietly stores nothing at all and looks like it is working;
    /// trying it means at most three failed calls for a genuinely odd
    /// attachment, and the ladder stops there of its own accord. One of those
    /// failures is silent and total, the other is loud and bounded.
    /// </para>
    /// </summary>
    public static Verdict Wanted(string kind, long sizeBytes)
    {
        if (kind == Kind.Reference)
            return new(false, "เป็นลิงก์ไปยังไฟล์ใน OneDrive/SharePoint ไม่ใช่ไฟล์แนบ จึงไม่ได้เก็บสำเนาไว้");
        if (kind == Kind.Item)
            return new(false, "เป็นอีเมลที่แนบมาในอีเมล ไม่ใช่ไฟล์ จึงไม่ได้เก็บเป็นเอกสาร");
        if (sizeBytes <= 0)
            return new(false, "ไฟล์แนบว่าง (0 ไบต์) ตามที่ Microsoft Graph รายงาน");
        if (sizeBytes > MaxBytes)
            return new(false, $"ไฟล์ใหญ่เกิน {MaxBytes / (1024 * 1024)} MB จึงไม่ได้เก็บเข้าคลังเอกสาร");

        return Go;
    }

    /// <summary>
    /// What to do about one failed download.
    ///
    /// <para>
    /// The message queue's decision, unchanged — a configuration fault pauses
    /// the pass, a thing that has gone finishes the row, and only genuine
    /// transient trouble spends an attempt. Deferring to it rather than
    /// restating it is the whole reason it is a separate rule: two ladders for
    /// the same set of Graph error codes would disagree within a month, and the
    /// symptom would be attachments retrying against a consent that the message
    /// queue had already correctly stopped for.
    /// </para>
    /// </summary>
    public static string Decide(string? code, bool ok, int attempts) =>
        MailQueue.Decide(code, ok, attempts);

    /* ------------------------------------------------------------- filing */

    /// <summary>
    /// The year a mail attachment is filed under: the message's own arrival.
    ///
    /// Not the clock. A file pulled in a retry the following January belongs
    /// with the mail it came on, or a year-end backlog scatters one week's
    /// paperwork across two trees.
    /// </summary>
    public static string Year(DateTimeOffset receivedAt) =>
        Stamp(receivedAt).Year.ToString("D4");

    /// <summary>The month folder, <c>2026-09</c>, from the same instant.</summary>
    public static string Period(DateTimeOffset receivedAt) =>
        Stamp(receivedAt).ToString("yyyy-MM");

    /// <summary>
    /// A message with no arrival time recorded is filed under now.
    ///
    /// It should not happen — the webhook stamps one and the fetch replaces it
    /// with Graph's — but <c>default</c> would file the message under the year
    /// 1, in a folder nobody will ever look in, which is a worse answer than
    /// approximately right.
    /// </summary>
    private static DateTimeOffset Stamp(DateTimeOffset receivedAt) =>
        receivedAt == default ? DateTimeOffset.UtcNow : receivedAt.ToUniversalTime();

    /// <summary>
    /// Who the document register records as having filed it.
    ///
    /// Not the sender: <c>UploadedBy</c> answers "who put this in SCMOS", and a
    /// customer who emailed a delivery order did not. Naming the integration
    /// keeps the audit trail honest about the fact that nobody chose to file
    /// this — it arrived.
    /// </summary>
    public const string FiledBy = "Outlook (อัตโนมัติ)";

    /// <summary>The document kind every mail attachment is stored under.</summary>
    public const string DocumentKind = "email-attachment";

    /// <summary>
    /// The document register's fourth scope, beside job, supplier and driver.
    ///
    /// A mail attachment's owner is the message, and it stays the message even
    /// after somebody says which job the message is about — the job key is
    /// added to the row, but the scope is not rewritten, because the file's
    /// provenance is that it arrived rather than that somebody filed it.
    /// </summary>
    public const string Scope = "mail";

    /// <summary>
    /// The folder label. The mail tree has no folders — it is a year and a
    /// month — so this names the tree rather than a place inside it, which is
    /// what the documents screen groups on.
    /// </summary>
    public const string Folder = "Mail";
}
