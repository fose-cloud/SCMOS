namespace Scmos.Api.Rules;

/// <summary>
/// What Graph's answer actually means, in words somebody can act on.
///
/// <para>
/// The plan says this integration will get stuck at the first real mailbox
/// read, and the reason is that <b>three different faults return 403</b>:
/// nobody has consented to <c>Mail.Read</c>, consent exists but Exchange RBAC
/// does not cover this mailbox, or the mailbox is not what anybody thought it
/// was. An administrator reading "403 Forbidden" learns nothing about which.
/// </para>
///
/// <para>
/// <see cref="GraphToken"/> removes the first of the three before any call is
/// made — the <c>roles</c> claim says whether consent was given. So by the time
/// a 403 arrives here, <paramref name="consented"/> tells the two remaining
/// cases apart, and the answer is a sentence naming the PowerShell cmdlet that
/// fixes it rather than a status code.
/// </para>
///
/// <para>
/// Pure, so <c>--check-graph</c> proves every branch with no mailbox and no
/// network. That matters here more than usual: the whole value of this file is
/// what it says on the day something is wrong, which is the day nobody wants to
/// be finding out it says the wrong thing.
/// </para>
/// </summary>
public static class GraphDiagnosis
{
    /// <summary>What went wrong, for the screen to branch on rather than parse Thai.</summary>
    public static class Code
    {
        public const string NotApproved = "not_approved";
        public const string NoToken = "no_token";
        public const string NoConsent = "no_consent";
        public const string NotScoped = "not_scoped";
        public const string NotFound = "not_found";
        public const string Rejected = "rejected";
        public const string Throttled = "throttled";
        public const string GraphError = "graph_error";
        public const string Unexpected = "unexpected";
        public const string Connected = "connected";
        public const string Empty = "empty";
    }

    /// <param name="Code">One of <see cref="Code"/>.</param>
    /// <param name="Message">For an administrator, in Thai, naming the fix where there is one.</param>
    /// <param name="Ok">Whether mail can be read from this mailbox.</param>
    public record Finding(string Code, string Message, bool Ok);

    /// <summary>The mailbox is not on this deployment's approved list.</summary>
    public static readonly Finding NotApproved = new(Code.NotApproved,
        "ตู้จดหมายนี้ไม่ได้อยู่ใน Graph__Mailboxes — deployment นี้ไม่อนุญาตให้อ่าน", false);

    /// <summary>Entra would not issue a token at all.</summary>
    public static readonly Finding NoToken = new(Code.NoToken,
        "ขอ token จาก Microsoft Graph ไม่ได้ — API ยังไม่มี managed identity หรือยังต่อ Entra ไม่ได้", false);

    /// <summary>Connected, and the mailbox is genuinely empty — which is a pass.</summary>
    public static readonly Finding Empty = new(Code.Empty,
        "ต่อกับตู้จดหมายได้แล้ว แต่ในตู้ยังไม่มีเมล", true);

    /// <summary>Connected, and a message came back.</summary>
    public static readonly Finding Connected = new(Code.Connected,
        "อ่านเมลจากตู้จดหมายนี้ได้", true);

    /// <summary>
    /// What an HTTP status from Graph means for this mailbox.
    /// </summary>
    /// <param name="status">The status Graph returned.</param>
    /// <param name="consented">
    /// Whether the token carried <c>Mail.Read</c>. This is the whole reason a
    /// 403 can be diagnosed rather than reported: without it, "no consent" and
    /// "consent but no Exchange scope" are the same answer.
    /// </param>
    public static Finding ForStatus(int status, bool consented) => status switch
    {
        // 200 says the call worked. Whether anything was in the mailbox is the
        // caller's to say — an empty mailbox is a working connection.
        >= 200 and < 300 => Connected,

        // Consent is the fault Graph reports as 403 when it has not been given.
        403 when !consented => new(Code.NoConsent,
            "ยังไม่ได้ admin consent สิทธิ์ Mail.Read ให้ managed identity ของ API", false),

        // And this is the one the whole file exists for. The permission is
        // granted tenant-wide; Exchange is refusing this particular mailbox,
        // which means the RBAC scoping is missing or does not include it.
        403 => new(Code.NotScoped,
            "consent ให้ Mail.Read แล้ว แต่ Exchange ยังไม่อนุญาตให้ identity นี้อ่านตู้นี้ — "
            + "ต้อง scope ด้วย New-ManagementRoleAssignment ให้ครอบคลุมตู้จดหมายนี้", false),

        // A tenant-wide grant with no RBAC scoping reads every mailbox, so a
        // 404 here is far more likely a misspelt address than a permission
        // problem wearing a different number.
        404 => new(Code.NotFound,
            "Microsoft Graph ไม่รู้จักตู้จดหมายนี้ — ตรวจว่าสะกดถูก และเป็น mailbox จริงไม่ใช่ alias", false),

        401 => new(Code.Rejected,
            "Microsoft Graph ปฏิเสธ token (401) — ไม่ใช่เรื่องสิทธิ์ แต่เป็นเรื่อง identity เอง", false),

        // Not a fault to fix. Saying so stops somebody changing a permission
        // that was never wrong.
        429 => new(Code.Throttled,
            "Microsoft Graph จำกัดจำนวนคำขออยู่ (429) — ไม่ใช่ปัญหาการตั้งค่า รอสักครู่แล้วลองใหม่", false),

        >= 500 => new(Code.GraphError,
            $"Microsoft Graph ขัดข้อง ({status}) — ไม่ใช่ปัญหาการตั้งค่า ลองใหม่อีกครั้ง", false),

        _ => new(Code.Unexpected, $"Microsoft Graph ตอบ {status}", false),
    };
}
