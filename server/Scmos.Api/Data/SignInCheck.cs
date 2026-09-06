using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the sign-in strength rule against fixtures, with <c>--check-signin</c>.
///
/// <para>
/// Two failures matter here and they point in opposite directions. If "cannot
/// tell" is read as "was multi-factor", enforcement protects nothing and says
/// it does. If it is read as "was single factor" while the policy only records,
/// the audit trail accuses people of signing in weakly on the evidence of a
/// claim that never arrived. Both are quiet, and both are checked below.
/// </para>
/// </summary>
public static class SignInCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-signin")) return null;

        var failed = 0;
        Console.WriteLine("How the sign-in is read, and what it is allowed to refuse.");
        Console.WriteLine();

        /* ---- reading the claim ---- */
        failed += Say("an authenticator approval is multi-factor",
            SignIn.Read(["pwd", "mfa"]), SignInStrength.MultiFactor);
        failed += Say("a hardware key is reported the same way",
            SignIn.Read(["fido", "mfa"]), SignInStrength.MultiFactor);
        failed += Say("freshly proven counts, being stronger and not weaker",
            SignIn.Read(["ngcmfa"]), SignInStrength.MultiFactor);
        failed += Say("a password alone is one factor",
            SignIn.Read(["pwd"]), SignInStrength.SingleFactor);
        failed += Say("windows sign-on alone is one factor",
            SignIn.Read(["wia"]), SignInStrength.SingleFactor);
        // The directory chooses the casing and the order; neither is a fact
        // about the sign-in.
        failed += Say("casing is the directory's business",
            SignIn.Read(["PWD", "MFA"]), SignInStrength.MultiFactor);
        failed += Say("so is the order",
            SignIn.Read(["mfa", "pwd"]), SignInStrength.MultiFactor);

        Console.WriteLine();
        // The distinction the whole design rests on.
        failed += Say("no claim at all is not a weak sign-in, it is no evidence",
            SignIn.Read(null), SignInStrength.Unknown);
        failed += Say("nor is an empty list",
            SignIn.Read([]), SignInStrength.Unknown);
        failed += Say("nor a list of blanks",
            SignIn.Read(["", "  "]), SignInStrength.Unknown);

        /* ---- what the policy refuses ---- */
        Console.WriteLine();
        foreach (var strength in new[]
                 { SignInStrength.Unknown, SignInStrength.SingleFactor, SignInStrength.MultiFactor })
        {
            failed += Say($"recording refuses nothing, even at {strength}",
                SignIn.Allows(SignInPolicy.Record, strength, Capability.EditRates), true);
        }

        Console.WriteLine();
        failed += Say("requiring lets a proven second factor through",
            SignIn.Allows(SignInPolicy.Require, SignInStrength.MultiFactor, Capability.EditRates), true);
        failed += Say("requiring refuses a single factor",
            SignIn.Allows(SignInPolicy.Require, SignInStrength.SingleFactor, Capability.EditRates), false);
        // An enforcement satisfied by silence is satisfied by a directory that
        // stops mentioning it, which is not an enforcement.
        failed += Say("and refuses what it cannot tell",
            SignIn.Allows(SignInPolicy.Require, SignInStrength.Unknown, Capability.EditRates), false);

        /* ---- the line between guarded and everyday ---- */
        Console.WriteLine();
        failed += Say("changing a rate is guarded", SignIn.IsGuarded(Capability.EditRates), true);
        failed += Say("replacing the register is guarded",
            SignIn.IsGuarded(Capability.AdministerData), true);
        failed += Say("agreeing a document may be destroyed is guarded",
            SignIn.IsGuarded(Capability.ApproveRetention), true);
        // The day's work is not. A system that asks for a phone before every
        // keystroke is one people find a way around.
        failed += Say("keying a job is not", SignIn.IsGuarded(Capability.EditOwnJobs), false);
        failed += Say("uploading a document is not",
            SignIn.IsGuarded(Capability.UploadDocuments), false);
        failed += Say("recording a quotation is not",
            SignIn.IsGuarded(Capability.QuoteToSheet), false);
        failed += Say("an unguarded action is allowed even at Require and Unknown",
            SignIn.Allows(SignInPolicy.Require, SignInStrength.Unknown, Capability.EditOwnJobs), true);

        /* ---- the setting fails to the safe value ---- */
        Console.WriteLine();
        failed += Say("the strict setting is spelled Require",
            SignIn.ReadPolicy("Require"), SignInPolicy.Require);
        failed += Say("and is not case sensitive", SignIn.ReadPolicy("require"), SignInPolicy.Require);
        // A misspelling must cost nobody their afternoon.
        failed += Say("a typo records rather than refuses",
            SignIn.ReadPolicy("Requrie"), SignInPolicy.Record);
        failed += Say("so does an unset setting", SignIn.ReadPolicy(null), SignInPolicy.Record);
        failed += Say("and the default is to record", SignIn.ReadPolicy(""), SignInPolicy.Record);

        /* ---- what the audit row says ---- */
        Console.WriteLine();
        failed += Say("the trail keeps the methods, not only the verdict",
            SignIn.Describe(SignInStrength.MultiFactor, ["pwd", "mfa"]), "mfa:pwd+mfa");
        failed += Say("a single factor says so", SignIn.Describe(SignInStrength.SingleFactor, ["pwd"]),
            "single:pwd");
        failed += Say("and no evidence says that instead of guessing",
            SignIn.Describe(SignInStrength.Unknown, []), "unknown");
        // The column is fixed width and the directory is not.
        var long_ = SignIn.Describe(SignInStrength.MultiFactor,
            Enumerable.Range(0, 40).Select(one => $"method{one}"));
        failed += Say("a talkative directory cannot overflow the column", long_.Length <= 60, true);

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "Sign-in strength is read honestly, and refuses only what it was told to."
            : $"{failed} problem(s).");
        return failed == 0 ? 0 : 1;
    }

    private static int Say<T>(string why, T got, T want)
    {
        var ok = EqualityComparer<T>.Default.Equals(got, want);
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {why,-62} {(ok ? "" : $"got {got}  want {want}")}");
        return ok ? 0 : 1;
    }
}
