namespace Scmos.Api.Rules;

/// <summary>
/// How hard it was to become the person making this request.
///
/// SCMOS does not check anybody's password and never has: Entra ID does that at
/// the App Service door and hands on a verified principal. So this is not an
/// authentication mechanism — it is the system reading what Entra already did,
/// so that a rate change can be answered for later and so the heaviest actions
/// can insist on more than one factor even while the tenant as a whole does not.
///
/// <para>
/// Three states rather than two, and the third is the point. "We know this was
/// one factor" and "we cannot tell" are different claims, and only the first is
/// evidence of anything. A tenant that does not emit <c>amr</c> would otherwise
/// look exactly like a tenant where nobody uses an authenticator, and the
/// difference decides whether turning enforcement on protects the register or
/// locks the whole team out of it.
/// </para>
/// </summary>
public enum SignInStrength
{
    /// <summary>No <c>amr</c> claim arrived. Nothing is known either way.</summary>
    Unknown = 0,

    /// <summary>Methods were named and none of them is a second factor.</summary>
    SingleFactor = 1,

    /// <summary>Entra says multi-factor authentication was satisfied.</summary>
    MultiFactor = 2,
}

/// <summary>
/// What the deployment does about a single-factor sign-in.
///
/// Two settings, not three, because a middle one would be a setting nobody
/// could describe. Either the strength is recorded and nothing is refused, or
/// an action is refused unless multi-factor is proven.
/// </summary>
public enum SignInPolicy
{
    /// <summary>
    /// Record it, refuse nothing. The default, and it must stay the default.
    ///
    /// Whether <c>amr</c> reaches this application at all depends on tenant
    /// configuration nobody here can read from the code. Shipping enforcement
    /// switched on would be shipping a guess about somebody else's directory,
    /// and the failure mode is every manager losing the ability to change a
    /// rate. Look at <c>/api/me</c> on a real signed-in session first — it says
    /// what actually arrived — and turn this on once that is known.
    /// </summary>
    Record = 0,

    /// <summary>
    /// Refuse the guarded actions unless multi-factor is proven.
    ///
    /// <see cref="SignInStrength.Unknown"/> is refused along with
    /// <see cref="SignInStrength.SingleFactor"/>. An enforcement that accepted
    /// "cannot tell" would be satisfied by a directory that simply stopped
    /// mentioning it, which is not enforcement.
    /// </summary>
    Require = 1,
}

public static class SignIn
{
    /// <summary>
    /// The <c>amr</c> values Entra uses to say a second factor was satisfied.
    ///
    /// <c>mfa</c> is the one that arrives for an authenticator approval, a
    /// hardware key or a phone sign-in alike — Entra reports the outcome rather
    /// than the brand. <c>ngcmfa</c> and <c>mngcmfa</c> are its "freshly proven"
    /// variants, asked for by privileged operations; they are stronger than
    /// <c>mfa</c>, never weaker, so they count too.
    /// </summary>
    private static readonly string[] SecondFactor = ["mfa", "ngcmfa", "mngcmfa"];

    /// <summary>
    /// Reads the <c>amr</c> claim. Order and casing are the directory's business.
    /// </summary>
    public static SignInStrength Read(IEnumerable<string>? methods)
    {
        var named = (methods ?? [])
            .Select(one => (one ?? "").Trim())
            .Where(one => one.Length > 0)
            .ToList();

        if (named.Count == 0) return SignInStrength.Unknown;

        return named.Any(one => SecondFactor.Contains(one, StringComparer.OrdinalIgnoreCase))
            ? SignInStrength.MultiFactor
            : SignInStrength.SingleFactor;
    }

    /// <summary>
    /// The <c>amr</c> claim as it is written into an audit row.
    ///
    /// The methods themselves rather than only the verdict, because "pwd" and
    /// "fido" are different answers to "how did they get in" and the verdict
    /// collapses them. Bounded, because this lands in a fixed-width column and
    /// a directory is free to send a longer list than anybody expects.
    /// </summary>
    public static string Describe(SignInStrength strength, IEnumerable<string>? methods)
    {
        var named = string.Join("+", (methods ?? [])
            .Select(one => (one ?? "").Trim().ToLowerInvariant())
            .Where(one => one.Length > 0)
            .Distinct()
            .Take(6));

        var verdict = strength switch
        {
            SignInStrength.MultiFactor => "mfa",
            SignInStrength.SingleFactor => "single",
            _ => "unknown",
        };

        var text = named.Length > 0 ? $"{verdict}:{named}" : verdict;
        return text.Length <= 60 ? text : text[..60];
    }

    /// <summary>
    /// The actions that a second factor is worth insisting on.
    ///
    /// Not everything, and the line is drawn where the damage is: the ones here
    /// move money or cannot be undone. Changing a rate rewrites what eighteen
    /// carriers are paid; administering data replaces the register wholesale;
    /// approving retention agrees that documents may be destroyed. Everything
    /// else — keying a job, uploading a document, recording a quotation — is
    /// the day's work, and a system that asks for a phone before every one of
    /// them is a system people find a way around.
    /// </summary>
    public static readonly Capability[] Guarded =
    [
        Capability.EditRates,
        Capability.AdministerData,
        Capability.ApproveRetention,
    ];

    public static bool IsGuarded(Capability capability) => Guarded.Contains(capability);

    /// <summary>
    /// Whether this sign-in may perform this action.
    ///
    /// Deliberately answers "allowed" for everything the policy does not guard,
    /// so that a caller which asks about the wrong capability fails open into
    /// the permission system rather than closed into a refusal nobody expects.
    /// The capability check itself is the gate; this is a second condition on
    /// three of them.
    /// </summary>
    public static bool Allows(SignInPolicy policy, SignInStrength strength, Capability capability)
    {
        if (policy != SignInPolicy.Require) return true;
        if (!IsGuarded(capability)) return true;
        return strength == SignInStrength.MultiFactor;
    }

    /// <summary>What to tell somebody who was refused, in the words they can act on.</summary>
    public static string Refusal(SignInStrength strength) => strength switch
    {
        SignInStrength.SingleFactor =>
            "งานนี้ต้องยืนยันตัวตนสองชั้น — ออกจากระบบแล้วเข้าใหม่ด้วย Microsoft Authenticator",
        _ =>
            "ระบบตรวจไม่พบการยืนยันตัวตนสองชั้นของบัญชีนี้ — ออกจากระบบแล้วเข้าใหม่ หรือติดต่อผู้ดูแลระบบ",
    };

    /// <summary>The setting's spelling, so a typo reads as the safe value rather than the strict one.</summary>
    public static SignInPolicy ReadPolicy(string? setting) =>
        string.Equals((setting ?? "").Trim(), "Require", StringComparison.OrdinalIgnoreCase)
            ? SignInPolicy.Require
            : SignInPolicy.Record;
}
