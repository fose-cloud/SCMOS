namespace Scmos.Api.Data;

/// <summary>
/// The Communication Center's tables — a shared mailbox, its messages, and what
/// SCMOS has made of them.
///
/// <para>
/// Seven, not the specification's ten. Three of the ten are already answered by
/// something the register has: the actions taken on a message are
/// <c>AuditEvent</c>, the processing log is a status on the message plus App
/// Insights, and an attachment's bytes are a <c>StoredDocument</c> in Blob.
/// <c>email_ai_analysis</c> is Phase 2 by the specification's own sequencing and
/// is not created here — a table nothing writes to is a table that gets a schema
/// nobody has thought about.
/// </para>
///
/// <para>
/// <b>The plan says six and lists seven.</b> The seventh is
/// <see cref="EmailAttachment"/>: what moved to Blob is the <i>bytes</i>, and a
/// row is still needed to say the file's name, its size, its type, which message
/// it arrived on and which stored document holds it. Counting the bytes as the
/// record is what produced the arithmetic.
/// </para>
///
/// <para>
/// Nothing here references a job by a foreign key. The register's jobs are rows
/// in <c>operation_jobs</c> keyed by a string the workbooks decide, and a
/// database-level constraint from a mailbox to one would mean a message could
/// block a job from being deleted. The link is its own table with its own
/// confidence, which is also what lets a link be a suggestion rather than a
/// fact.
/// </para>
/// </summary>
public class Mailbox
{
    public long Id { get; set; }

    /// <summary>The address as Graph knows it — ops@leschaco.co.th.</summary>
    public string Address { get; set; } = "";

    /// <summary>What to call it on screen.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// The Graph user or group id the address resolves to.
    ///
    /// Held beside the address because an address can be reassigned and the id
    /// cannot. A subscription renewed against the address alone would follow the
    /// name to whoever holds it next.
    /// </summary>
    public string GraphUserId { get; set; } = "";

    /// <summary>Which folder is watched. Empty means the whole mailbox.</summary>
    public string FolderId { get; set; } = "";

    /// <summary>Off until somebody connects it, like every other integration here.</summary>
    public bool IsActive { get; set; }

    /// <summary>When a message was last read out of it, for the screen to show staleness.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One message, kept as it arrived.
///
/// <para>
/// The body is stored because the extractor reads it and because a person
/// reviewing a link has to see what it was read from. It is stored as text and
/// as HTML where Graph gives both: the text is what rules run over, the HTML is
/// what a person is shown, and deriving one from the other in either direction
/// loses something.
/// </para>
/// </summary>
public class Email
{
    public long Id { get; set; }

    public long MailboxId { get; set; }

    /// <summary>
    /// Graph's own id for the message.
    ///
    /// Unique with the mailbox, which is the whole of idempotency here — Graph
    /// delivers the same notification more than once and the specification is
    /// right to insist on it.
    /// </summary>
    public string GraphMessageId { get; set; } = "";

    /// <summary>The conversation it belongs to, so a thread can be shown together.</summary>
    public string ConversationId { get; set; } = "";

    /// <summary>RFC 822 message id, for matching against something outside Graph.</summary>
    public string InternetMessageId { get; set; } = "";

    public string Subject { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "";

    /// <summary>The plain text the extractor reads.</summary>
    public string BodyText { get; set; } = "";

    /// <summary>The HTML a person is shown. Empty when Graph gave only text.</summary>
    public string BodyHtml { get; set; } = "";

    /// <summary>When the sender sent it, and when it reached the mailbox.</summary>
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }

    public bool HasAttachments { get; set; }

    /// <summary>RECEIVED · PROCESSING · PROCESSED · NEED_REVIEW · FAILED.</summary>
    public string ProcessingStatus { get; set; } = MailProcessing.Received;

    /// <summary>
    /// When the worker last had this row in its hands.
    ///
    /// Stamped on claim as well as on completion, for the reason the LINE worker
    /// records: arrival time cannot tell a row being worked on from one whose
    /// container went away mid-claim, because a backlog is full of old rows.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    public string ErrorCode { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public int RetryCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Who a message was addressed to, one row each.
///
/// A row rather than a comma-separated column because "which messages went to
/// this customer" is a question the screen will be asked, and a LIKE over a
/// joined string answers it wrongly the first time an address contains another.
/// </summary>
public class EmailParticipant
{
    public long Id { get; set; }
    public long EmailId { get; set; }

    /// <summary>TO · CC · BCC.</summary>
    public string Kind { get; set; } = MailParticipant.To;

    public string Address { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

/// <summary>
/// What arrived attached, as a record — the bytes live in Blob.
///
/// <para>
/// <see cref="StoredDocumentId"/> is zero until the file has been fetched and
/// stored, which is deliberate: the message is recorded when it arrives and the
/// attachments are pulled afterwards, so a row with no document yet is the
/// ordinary state rather than a fault.
/// </para>
/// </summary>
public class EmailAttachment
{
    public long Id { get; set; }
    public long EmailId { get; set; }

    /// <summary>Graph's id for the attachment, so a re-fetch does not duplicate it.</summary>
    public string GraphAttachmentId { get; set; } = "";

    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }

    /// <summary>The stored document holding the bytes, or 0 until it has been fetched.</summary>
    public long StoredDocumentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// An identifier the extractor found in a message.
///
/// <para>
/// One row per find, carrying where it was found and how well formed it was,
/// because that is what the matcher weighs — see <c>EmailExtraction</c>, which
/// produces exactly these fields. Kept rather than recomputed so a link can be
/// explained months later against the rules as they were, not as they have since
/// become.
/// </para>
/// </summary>
public class EmailEntity
{
    public long Id { get; set; }
    public long EmailId { get; set; }

    /// <summary>JOB_CODE · CONTAINER · BOOKING · BL.</summary>
    public string Kind { get; set; } = "";

    /// <summary>The identifier, trimmed and upper-cased as it will be matched.</summary>
    public string Value { get; set; } = "";

    /// <summary>Whether the subject carried it. A body is the thread; a subject is this message.</summary>
    public bool InSubject { get; set; }

    /// <summary>Whether the text named it — "Container No: X" rather than a bare token.</summary>
    public bool Labelled { get; set; }

    /// <summary>Whether it passes its own format's self-check. Only containers have one.</summary>
    public bool WellFormed { get; set; }
}

/// <summary>
/// A message attached to a job, and how sure anybody is.
///
/// <para>
/// The confidence is the specification's, kept: at or above 0.95 it is linked
/// without asking, from 0.70 it is offered for a person to confirm, and below
/// that it is not offered at all. An email silently attached to the wrong job is
/// worse than one left unattached, because the second is visible.
/// </para>
///
/// <para>
/// <see cref="ConfirmedBy"/> empty means the link is the machine's. A person's
/// name there is the record that somebody agreed, which is what makes an
/// automatic link and an agreed one tellable apart afterwards.
/// </para>
/// </summary>
public class EmailJobLink
{
    public long Id { get; set; }
    public long EmailId { get; set; }

    /// <summary>The register's own key. Not a foreign key — see the note on MailEntities.</summary>
    public string JobKey { get; set; } = "";

    /// <summary>Which identifier carried the link, so it can be explained.</summary>
    public string MatchedOn { get; set; } = "";
    public string MatchedValue { get; set; } = "";

    /// <summary>0 to 1. Read against the thresholds above, never rounded into them.</summary>
    public double Confidence { get; set; }

    /// <summary>SUGGESTED · CONFIRMED · REJECTED.</summary>
    public string Status { get; set; } = MailLink.Suggested;

    /// <summary>Who agreed, or empty while it is the machine's own.</summary>
    public string ConfirmedBy { get; set; } = "";
    public DateTimeOffset? ConfirmedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A Graph subscription, and when it dies.
///
/// <para>
/// Graph expires these in days and stops delivering without saying so, so the
/// expiry is stored and renewed against rather than assumed. A subscription that
/// lapsed unnoticed is a mailbox that has quietly stopped being read, which
/// looks exactly like a quiet mailbox.
/// </para>
/// </summary>
public class GraphSubscription
{
    public long Id { get; set; }
    public long MailboxId { get; set; }

    /// <summary>Graph's id, needed to renew or delete it.</summary>
    public string SubscriptionId { get; set; } = "";

    /// <summary>What it watches and where it delivers.</summary>
    public string Resource { get; set; } = "";
    public string NotificationUrl { get; set; } = "";

    /// <summary>
    /// The secret Graph echoes back on every notification.
    ///
    /// Compared in fixed time by the webhook, the way the LINE signature is. It
    /// is the only thing that distinguishes a genuine delivery from anybody who
    /// found the URL.
    /// </summary>
    public string ClientState { get; set; } = "";

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastRenewedAt { get; set; }

    /// <summary>ACTIVE · EXPIRED · FAILED · DELETED.</summary>
    public string Status { get; set; } = MailSubscription.Active;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Where a message has got to. The same ladder the LINE queue uses.</summary>
public static class MailProcessing
{
    public const string Received = "RECEIVED";
    public const string Processing = "PROCESSING";
    public const string Processed = "PROCESSED";
    public const string NeedReview = "NEED_REVIEW";
    public const string Failed = "FAILED";
}

/// <summary>How somebody was addressed.</summary>
public static class MailParticipant
{
    public const string To = "TO";
    public const string Cc = "CC";
    public const string Bcc = "BCC";
}

/// <summary>Whether a link is the machine's guess or somebody's decision.</summary>
public static class MailLink
{
    public const string Suggested = "SUGGESTED";
    public const string Confirmed = "CONFIRMED";
    public const string Rejected = "REJECTED";

    /// <summary>At or above this a link is made without asking.</summary>
    public const double AutoLink = 0.95;

    /// <summary>Below this it is not offered at all.</summary>
    public const double Suggest = 0.70;
}

/// <summary>The life of a Graph subscription.</summary>
public static class MailSubscription
{
    public const string Active = "ACTIVE";
    public const string Expired = "EXPIRED";
    public const string Failed = "FAILED";
    public const string Deleted = "DELETED";
}
