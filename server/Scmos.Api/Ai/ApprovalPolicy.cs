using System.Security.Cryptography;
using System.Text;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai;

/// <summary>
/// Who may put a proposal in the assistant's approval queue, who may see
/// it, who may decide it, and when it goes stale — Phase 1E of the AI
/// platform plan (20 Sep 2026), the hardening the assessment listed as
/// S1/S2 before the queue carries anything a later agent recommends.
///
/// <para>
/// The queue existed from the first AI release with a sign-in gate on
/// listing and creation and a supervisor gate on the decision. That was
/// enough while nothing in it mattered. This class makes the rules
/// explicit and the same at every route: a proposal is made by an
/// internal account that could make the change itself; it is seen by its
/// requester and by approvers; it is decided by an approver who is not
/// the requester; it expires untouched; approving it records the exact
/// payload that was read, so "applied" can only ever name that payload;
/// and nothing here executes anything — applying is still a person's act,
/// recorded, not a business executor.
/// </para>
///
/// <para>Pure. The AI checks prove it offline.</para>
/// </summary>
public static class ApprovalPolicy
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Applied = "applied";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";

    /// <summary>A proposal nobody decides in a week is stale: the job, the rate, the case have moved on.</summary>
    public static readonly TimeSpan PendingTtl = TimeSpan.FromDays(7);

    /// <summary>An approval nobody applies in a week is stale for the same reason — the approver read a world that has changed.</summary>
    public static readonly TimeSpan ApprovedTtl = TimeSpan.FromDays(7);

    public const int MaxFields = 20;
    public const int MaxKeyLength = 40;
    public const int MaxValueLength = 500;
    public const int MaxPayloadBytes = 8 * 1024;
    public const int MaxSummaryLength = 500;

    /// <summary>The actions a person may take on a row, as the screen shows them and the routes accept them.</summary>
    public const string ActApprove = "approve";
    public const string ActReject = "reject";
    public const string ActCancel = "cancel";
    public const string ActApply = "apply";

    /// <summary>
    /// The capability a person needs before the assistant may draft a tool on
    /// their behalf: the one they would need to make the change by hand. A
    /// proposal from somebody who could not make the change is a way round
    /// the permission, not a proposal.
    /// </summary>
    public static Capability RequiredToRequest(Rules.AiToolDefinition tool) => tool.Name.ToLowerInvariant() switch
    {
        "update_shipment" => Capability.EditOwnJobs,
        "assign_supplier" => Capability.EditOwnJobs,
        "update_rate" => Capability.EditRates,
        "close_carpar" => Capability.CloseCarPar,
        // Mail leaves the building under the department's name: a supervisor's act.
        "send_email" => Capability.ApproveAi,
        _ => tool.Agent switch
        {
            AiPermissions.Operation => Capability.EditOwnJobs,
            AiPermissions.SupplierAgent => Capability.EditRates,
            AiPermissions.Safety => Capability.CloseCarPar,
            AiPermissions.Document => Capability.UploadDocuments,
            _ => Capability.ApproveAi,
        },
    };

    /// <summary>An internal account holding <see cref="Capability.ApproveAi"/> — a supervisor or above, never a carrier.</summary>
    public static bool IsApprover(AppUser? user) =>
        user is not null && AiPermissionPolicy.InternalUser(user) && user.Can(Capability.ApproveAi);

    /// <summary>
    /// Whether this person made the proposal. Rows from before 1E carry no
    /// requester id; for them the signature stored at the time is the match.
    /// </summary>
    public static bool IsRequester(AppUser user, Approval approval) =>
        approval.RequesterId.Length > 0
            ? approval.RequesterId == user.UserId
            : approval.RequestedBy.Length > 0 && approval.RequestedBy == user.Signature;

    /// <summary>Approvers see the queue; everybody else sees only what they asked for; a carrier sees nothing.</summary>
    public static bool CanSee(AppUser? user, Approval approval) =>
        user is not null && AiPermissionPolicy.InternalUser(user) && (IsApprover(user) || IsRequester(user, approval));

    /// <summary>
    /// Why a proposal may not be made, or null when it may: the account must
    /// be internal and hold the capability the tool stands in for; the
    /// summary and the fields must fit the row and carry nothing but plain
    /// text.
    /// </summary>
    public static string? RequestProblem(AppUser? user, Rules.AiToolDefinition tool, string summary,
        IReadOnlyDictionary<string, string>? fields)
    {
        if (user is null || !AiPermissionPolicy.InternalUser(user)) return "not-internal";
        if (!user.Can(RequiredToRequest(tool))) return "no-capability";
        if (summary.Length > MaxSummaryLength || HasControlCharacters(summary)) return "summary";
        return PayloadProblem(fields);
    }

    /// <summary>
    /// Why the fields cannot be a payload, or null: at most twenty, each key
    /// a short identifier, each value plain text of at most 500 characters,
    /// the whole under 8 KB. A key that names an identity or a permission is
    /// refused — a payload is the arguments of a tool, never who is asking.
    /// </summary>
    public static string? PayloadProblem(IReadOnlyDictionary<string, string>? fields)
    {
        if (fields is null) return null;
        if (fields.Count > MaxFields) return "too-many-fields";
        var bytes = 0;
        foreach (var (key, value) in fields)
        {
            if (!IsKey(key)) return $"key:{Truncate(key)}";
            if (ReservedKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) return $"reserved:{key}";
            if (value is null) return $"value:{key}";
            if (value.Length > MaxValueLength || HasControlCharacters(value)) return $"value:{key}";
            bytes += Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(value) + 6;
            if (bytes > MaxPayloadBytes) return "too-large";
        }
        return null;
    }

    /// <summary>Names a payload may not carry: an identity or a permission is decided by the server, never sent in.</summary>
    public static readonly string[] ReservedKeys =
        ["role", "roles", "userId", "user_id", "operatorId", "operator_id", "capability", "capabilities", "sql", "query", "token", "authorization"];

    /// <summary>SHA-256 of the payload as stored, lower-case hex — the fingerprint an approver reads and an applier must quote.</summary>
    public static string Hash(string payloadJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson ?? ""))).ToLowerInvariant();

    /// <summary>The fingerprint a row answers to: the one stored at creation, or — for a row from before 1E — the payload's own.</summary>
    public static string ExpectedHash(Approval approval) =>
        approval.PayloadHash.Length > 0 ? approval.PayloadHash : Hash(approval.Payload);

    /// <summary>When a row in this state goes stale, counted from the moment it entered the state.</summary>
    public static DateTimeOffset? ExpiryOf(string state, DateTimeOffset at) => state switch
    {
        Pending => at + PendingTtl,
        Approved => at + ApprovedTtl,
        _ => null,
    };

    /// <summary>A pending or approved row past its expiry. Decided, applied, cancelled and expired rows never expire again.</summary>
    public static bool IsExpired(Approval approval, DateTimeOffset now) =>
        approval.State is Pending or Approved && approval.ExpiresAt is { } at && at <= now;

    /// <summary>
    /// Why this person may not decide this row, or null: an approver who is
    /// not its requester, while it is pending and not stale. The requester
    /// approving their own proposal would be the assistant editing the job.
    /// </summary>
    public static string? DecideProblem(AppUser? user, Approval approval, DateTimeOffset now)
    {
        if (!IsApprover(user)) return "approver";
        if (IsRequester(user!, approval)) return "self";
        if (approval.State != Pending) return "state";
        if (IsExpired(approval, now)) return "expired";
        return null;
    }

    /// <summary>Why this person may not withdraw this row, or null: its requester or an approver, while it is pending and not stale.</summary>
    public static string? CancelProblem(AppUser? user, Approval approval, DateTimeOffset now)
    {
        if (user is null || !AiPermissionPolicy.InternalUser(user)) return "who";
        if (!IsRequester(user, approval) && !IsApprover(user)) return "who";
        if (approval.State != Pending) return "state";
        if (IsExpired(approval, now)) return "expired";
        return null;
    }

    /// <summary>
    /// Why this person may not record this row as applied, or null: an
    /// approver, the row approved and not stale, and the fingerprint they
    /// quote the one the approver read. A result typed by a client proves
    /// nothing about execution; this records that a person did the thing,
    /// and which thing.
    /// </summary>
    public static string? ApplyProblem(AppUser? user, Approval approval, string reviewedHash, DateTimeOffset now)
    {
        if (!IsApprover(user)) return "approver";
        if (approval.State != Approved) return "state";
        if (IsExpired(approval, now)) return "expired";
        if (!string.Equals((reviewedHash ?? "").Trim(), ExpectedHash(approval), StringComparison.OrdinalIgnoreCase)) return "hash";
        return null;
    }

    /// <summary>The actions this person may take on this row now — what the screen offers, and only that.</summary>
    public static IReadOnlyList<string> ActionsFor(AppUser? user, Approval approval, DateTimeOffset now)
    {
        var actions = new List<string>(3);
        if (DecideProblem(user, approval, now) is null) { actions.Add(ActApprove); actions.Add(ActReject); }
        if (CancelProblem(user, approval, now) is null) actions.Add(ActCancel);
        if (user is not null && approval.State == Approved && !IsExpired(approval, now) && IsApprover(user)) actions.Add(ActApply);
        return actions;
    }

    private static bool IsKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength) return false;
        if (!char.IsAsciiLetter(key[0])) return false;
        foreach (var c in key) if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        return true;
    }

    private static bool HasControlCharacters(string text)
    {
        foreach (var c in text) if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t') return true;
        return false;
    }

    private static string Truncate(string? text) => (text ?? "").Length > 20 ? text![..20] + "…" : text ?? "";
}
