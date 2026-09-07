namespace Scmos.Api.Data;

/*
 * What LINE needs stored, and nothing more.
 *
 * The plan called for four tables. Reading the schema first cut it to three:
 * WorkflowEvent is already the job's status history — job key, from stage, to
 * stage, note, who, when — and DelayRecord already carries a category, a
 * responsible party, and a ClassifiedBy of "rule · ai · human" with the words
 * the classifier matched. A LINE-driven delay is a DelayRecord classified by
 * rule. Adding a second history table beside one that already exists is how
 * this repository has produced two records of the same fact that disagree.
 *
 * So what is genuinely new is the LINE side of the bridge: which group belongs
 * to which supplier, which user is who, and the raw event itself.
 */

/// <summary>Where a status update came from. Stored on WorkflowEvent.</summary>
public static class EventSource
{
    /// <summary>Somebody working in SCMOS. The default, and what every row
    /// written before LINE existed is.</summary>
    public const string Scmos = "SCMOS";

    public const string Line = "LINE";
    public const string System = "SYSTEM";

    public static string Read(string? value)
    {
        var text = (value ?? "").Trim().ToUpperInvariant();
        return text is Line or System ? text : Scmos;
    }
}

/// <summary>
/// A LINE group, and the supplier whose work is discussed in it.
///
/// <para>
/// This mapping is the authorisation. Nothing in a message's text is ever
/// allowed to decide which supplier it is about — a vendor typing another
/// vendor's name is not permission, and a group that has not been mapped
/// cannot update anything at all.
/// </para>
///
/// <para>
/// It joins to <see cref="Supplier"/> by id rather than by name, because
/// SupplierAlias already exists to settle that "DGT" and "DGT Cross Haul Co.,
/// Ltd." are one company, and a second spelling-matching rule here would
/// eventually disagree with it.
/// </para>
/// </summary>
public class LineGroup
{
    public long Id { get; set; }

    /// <summary>LINE's own id for the group. The unique key.</summary>
    public string LineGroupId { get; set; } = "";

    /// <summary>What the operators call it, so the review screen reads like the phone.</summary>
    public string GroupName { get; set; } = "";

    /// <summary>The supplier this group speaks for. 0 until somebody maps it.</summary>
    public long SupplierId { get; set; }

    /// <summary>VENDOR · INTERNAL · CUSTOMER · OTHER.</summary>
    public string GroupType { get; set; } = LineGroupType.Vendor;

    /// <summary>
    /// Turned off rather than deleted.
    ///
    /// A group that stops being used still has months of messages pointing at
    /// it, and deleting the row would leave those unreadable. Inactive means
    /// new messages are stored and not acted on.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class LineGroupType
{
    public const string Vendor = "VENDOR";
    public const string Internal = "INTERNAL";
    public const string Customer = "CUSTOMER";
    public const string Other = "OTHER";
}

/// <summary>
/// A person in a LINE group, as far as we can know who they are.
///
/// <para>
/// Often we cannot. LINE gives a user id and, with permission, a display name;
/// it does not give an email. So this maps to a SCMOS staff id when the person
/// is one of ours and to a supplier when they are a vendor's driver, and to
/// neither when nobody has said. An unmapped user is not refused — the group
/// is what authorises — but the review screen shows who is unknown, because a
/// vendor group that suddenly has a stranger in it is worth noticing.
/// </para>
/// </summary>
public class LineUser
{
    public long Id { get; set; }

    /// <summary>LINE's own id for the person. The unique key.</summary>
    public string LineUserId { get; set; } = "";

    /// <summary>As LINE reports it. May be blank, and may change.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>The supplier they work for, when they are a vendor's. 0 otherwise.</summary>
    public long SupplierId { get; set; }

    /// <summary>The SCMOS staff id when they are one of ours — OP-01, SV-01. Empty otherwise.</summary>
    public string StaffId { get; set; } = "";

    /// <summary>DRIVER · VENDOR_OPERATOR · STAFF · UNKNOWN.</summary>
    public string Role { get; set; } = "UNKNOWN";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One webhook event, exactly as LINE delivered it.
///
/// <para>
/// The raw payload is kept before anything is done with it. That is not
/// caution for its own sake: when a message updates the wrong job, or fails to
/// update the right one, the only way to find out why is to read what actually
/// arrived — and by then the parser will have been changed.
/// </para>
///
/// <para>
/// <see cref="LineMessageId"/> is unique, and that is the whole of idempotency.
/// LINE retries a webhook it did not get a fast enough answer to, so the same
/// message will arrive more than once; the second insert loses on the index and
/// the worker never sees it twice.
/// </para>
/// </summary>
public class LineEvent
{
    public long Id { get; set; }

    /// <summary>LINE's webhook event id. Present on most events, not all.</summary>
    public string WebhookEventId { get; set; } = "";

    /// <summary>LINE's message id. Unique — see the note above.</summary>
    public string LineMessageId { get; set; } = "";

    /// <summary>The group it was sent in. Empty for a direct message.</summary>
    public string LineGroupId { get; set; } = "";

    /// <summary>Who sent it.</summary>
    public string LineUserId { get; set; } = "";

    /// <summary>text · image · sticker · … Only text is read; the rest are stored and ignored.</summary>
    public string MessageType { get; set; } = "";

    /// <summary>The message as typed. Never normalised in place — the parser works on a copy.</summary>
    public string RawText { get; set; } = "";

    /// <summary>The whole webhook body for this event, as JSON.</summary>
    public string RawPayload { get; set; } = "";

    /// <summary>When SCMOS received it. Not when the driver says the thing happened.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>RECEIVED · PROCESSING · PROCESSED · NEED_REVIEW · IGNORED · FAILED.</summary>
    public string ProcessingStatus { get; set; } = LineProcessing.Received;

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Why it stopped, in a word a screen can group by.</summary>
    public string ErrorCode { get; set; } = "";

    /// <summary>And in words a person can read.</summary>
    public string ErrorMessage { get; set; } = "";

    public int RetryCount { get; set; }

    /* ---- what the parser made of it, kept so a reviewer can disagree ---- */

    /// <summary>The job it was taken to be about. Empty when none was found.</summary>
    public string JobKey { get; set; } = "";

    /// <summary>The twelve-digit number read out of the text, before it was resolved.</summary>
    public string JobNumber { get; set; } = "";

    /// <summary>The status it was read as asking for.</summary>
    public string ParsedStatus { get; set; } = "";

    /// <summary>How much of the message was understood, 0 to 1.</summary>
    public double Confidence { get; set; }

    /// <summary>Which rules fired, and what gave the parser pause. Comma separated.</summary>
    public string MatchedRules { get; set; } = "";
    public string Warnings { get; set; } = "";
}

/// <summary>Where a stored event has got to.</summary>
public static class LineProcessing
{
    /// <summary>Stored by the webhook, not yet looked at.</summary>
    public const string Received = "RECEIVED";

    /// <summary>Claimed by a worker. See the note on claiming in LineEventWorker.</summary>
    public const string Processing = "PROCESSING";

    /// <summary>A job was updated.</summary>
    public const string Processed = "PROCESSED";

    /// <summary>Understood too poorly to act on. A person decides.</summary>
    public const string NeedReview = "NEED_REVIEW";

    /// <summary>Not a message this system is for — a sticker, a greeting.</summary>
    public const string Ignored = "IGNORED";

    /// <summary>Something broke that was not the message's fault.</summary>
    public const string Failed = "FAILED";
}
