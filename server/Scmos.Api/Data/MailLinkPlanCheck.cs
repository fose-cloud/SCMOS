using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// What re-matching a message writes, with <c>--check-links</c>.
///
/// <para>
/// A message is matched more than once — an abandoned claim goes back in the
/// queue, the catch-up finds mail the webhook already brought, the extractor is
/// improved and everything is read again. <b>The failure this looks for is
/// silent.</b> A confirmation quietly replaced by a fresh guess is a decision
/// that stopped counting, and nothing anywhere would say so: the link is still
/// there, still pointing at the right job, and only its status is a lie.
/// </para>
/// </summary>
public static class MailLinkPlanCheck
{
    private static EmailMatching.JobMatch Match(string job, double confidence) =>
        new(job, confidence, EmailMatching.DecisionFor(confidence), EmailExtraction.Kind.Container,
            "TEMU0404097", "");

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-links")) return null;

        var failed = 0;

        void Check(bool ok, string why)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        string ActionFor(IReadOnlyList<MailLinkPlan.Existing> existing,
            IReadOnlyList<EmailMatching.JobMatch> matches, string job) =>
            MailLinkPlan.For(existing, matches).FirstOrDefault(one => one.JobKey == job)?.Action ?? "NONE";

        Console.WriteLine();
        Console.WriteLine("Matching a message for the first time.");
        Console.WriteLine();

        Check(ActionFor([], [Match("J1", 0.96)], "J1") == MailLinkPlan.Do.Add,
            "a linked job with no row yet is written");
        Check(ActionFor([], [Match("J1", 0.80)], "J1") == MailLinkPlan.Do.Add,
            "and so is a suggested one");
        // Below the floor the specification says it is not offered, so there is
        // nothing to write and nothing to show.
        Check(ActionFor([], [Match("J1", 0.40)], "J1") == "NONE",
            "a job below the floor is not written at all");

        Console.WriteLine();
        Console.WriteLine("Matching it again — the machine's own guesses.");
        Console.WriteLine();

        var guess = new List<MailLinkPlan.Existing> { new("J1", MailLink.Suggested) };
        Check(ActionFor(guess, [Match("J1", 0.96)], "J1") == MailLinkPlan.Do.Update,
            "a guess is restated with today's numbers");
        // A stale suggestion left on screen is one somebody eventually confirms.
        Check(ActionFor(guess, [], "J1") == MailLinkPlan.Do.Remove,
            "a guess the rules no longer support is withdrawn");
        Check(ActionFor(guess, [Match("J1", 0.40)], "J1") == MailLinkPlan.Do.Remove,
            "and so is one that has fallen below the floor");

        Console.WriteLine();
        Console.WriteLine("And the decisions a person has made.");
        Console.WriteLine();

        var confirmed = new List<MailLinkPlan.Existing> { new("J1", MailLink.Confirmed) };
        var rejected = new List<MailLinkPlan.Existing> { new("J1", MailLink.Rejected) };

        Check(ActionFor(confirmed, [Match("J1", 0.96)], "J1") == MailLinkPlan.Do.Keep,
            "a confirmed link is left exactly as it is");
        Check(ActionFor(confirmed, [Match("J1", 0.72)], "J1") == MailLinkPlan.Do.Keep,
            "even when the rules are now less sure than the person was");
        // The one that matters most: re-reading must not take a confirmation
        // away because this pass happened to find nothing.
        Check(ActionFor(confirmed, [], "J1") == MailLinkPlan.Do.Keep,
            "and even when this pass found nothing at all");

        Check(ActionFor(rejected, [Match("J1", 0.99)], "J1") == MailLinkPlan.Do.Keep,
            "a rejected link stays rejected, however sure the rules become");
        Check(ActionFor(rejected, [], "J1") == MailLinkPlan.Do.Keep, "and stays rejected when nothing matches");

        // An unfamiliar value in that column is a reason to leave a row alone,
        // not a reason to overwrite it.
        var odd = new List<MailLinkPlan.Existing> { new("J1", "SOMETHING_ELSE") };
        Check(ActionFor(odd, [Match("J1", 0.96)], "J1") == MailLinkPlan.Do.Keep,
            "a status nobody recognises is left alone rather than overwritten");

        Console.WriteLine();
        Console.WriteLine("Running it twice changes nothing the second time.");
        Console.WriteLine();

        // The property the whole file exists for, stated as one line: whatever
        // the first pass wrote, the second pass must only restate.
        var matches = new[] { Match("J1", 0.96), Match("J2", 0.80) };
        var first = MailLinkPlan.For([], matches);
        Check(first.All(one => one.Action == MailLinkPlan.Do.Add), "the first pass writes both");

        var afterFirst = first.Select(one => new MailLinkPlan.Existing(one.JobKey, MailLink.Suggested)).ToList();
        var second = MailLinkPlan.For(afterFirst, matches);
        Check(second.All(one => one.Action == MailLinkPlan.Do.Update),
            "the second writes nothing new and takes nothing away");
        Check(second.Count == first.Count, "and touches the same jobs, no more");

        // Now somebody agrees with one of them, and it is read again.
        var afterPerson = new List<MailLinkPlan.Existing>
        {
            new("J1", MailLink.Confirmed),
            new("J2", MailLink.Suggested),
        };
        var third = MailLinkPlan.For(afterPerson, matches);
        Check(third.Single(one => one.JobKey == "J1").Action == MailLinkPlan.Do.Keep
            && third.Single(one => one.JobKey == "J2").Action == MailLinkPlan.Do.Update,
            "their decision survives the next pass while the guess beside it is restated");

        Console.WriteLine();
        Console.WriteLine("Whether anybody still has something to decide.");
        Console.WriteLine();

        Check(!MailLinkPlan.NeedsAPerson([Match("J1", 0.96)]),
            "one job linked cleanly needs nobody");
        Check(MailLinkPlan.NeedsAPerson([Match("J1", 0.80)]),
            "a suggestion needs somebody to confirm it");
        // Two confident answers to one question is a question.
        Check(MailLinkPlan.NeedsAPerson([Match("J1", 0.96), Match("J2", 0.96)]),
            "two jobs that both cleared the bar need somebody to choose");
        Check(MailLinkPlan.NeedsAPerson([Match("J1", 0.96), Match("J2", 0.80)]),
            "so does a link with a suggestion beside it");

        // Mail about no job we hold is not a task. Marking every one of them
        // for review is how the flag stops meaning anything.
        Check(!MailLinkPlan.NeedsAPerson([]), "mail naming nothing is not a review task");
        Check(!MailLinkPlan.NeedsAPerson([Match("J1", 0.40)]),
            "and neither is mail whose only candidate is below the floor");

        Console.WriteLine();
        Console.WriteLine("What counts as somebody's decision.");
        Console.WriteLine();

        Check(!MailLinkPlan.IsPersons(MailLink.Suggested), "SUGGESTED is the machine's own");
        Check(MailLinkPlan.IsPersons(MailLink.Confirmed), "CONFIRMED is a person's");
        Check(MailLinkPlan.IsPersons(MailLink.Rejected), "so is REJECTED");
        Check(MailLinkPlan.IsPersons(""), "and so is anything else, which is the safe way round");

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "All mail link checks passed."
            : $"{failed} mail link check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
