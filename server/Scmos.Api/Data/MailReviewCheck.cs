using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// What the Communication Center shows and what a decision changes, with
/// <c>--check-review</c>.
///
/// <para>
/// The badge on the menu, the WAITING list and the message page all answer the
/// same two questions. A badge saying 5 over a list of 3 is the kind of thing
/// people stop believing and then stop looking at, so both questions are
/// answered in one place and that place is checked here.
/// </para>
/// </summary>
public static class MailReviewCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-review")) return null;

        var failed = 0;

        void Check(bool ok, string why)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        Console.WriteLine();
        Console.WriteLine("What a message becomes once its links are settled.");
        Console.WriteLine();

        var received = MailProcessing.Received;

        Check(MailReview.StatusAfter([MailLink.Suggested], received) == MailProcessing.NeedReview,
            "a suggestion nobody has settled leaves it waiting");
        Check(MailReview.StatusAfter([MailLink.Confirmed], received) == MailProcessing.Processed,
            "a confirmed link finishes it");
        Check(MailReview.StatusAfter([MailLink.Rejected], received) == MailProcessing.Processed,
            "so does a rejection — a decision is a decision either way");

        // The sequence somebody actually walks through.
        Check(MailReview.StatusAfter([MailLink.Confirmed, MailLink.Suggested], received)
            == MailProcessing.NeedReview,
            "settling one of two leaves the other still waiting");
        Check(MailReview.StatusAfter([MailLink.Confirmed, MailLink.Rejected], received)
            == MailProcessing.Processed,
            "and settling both finishes it");

        // Mail about no job we hold is not a task. It is ordinary mail.
        Check(MailReview.StatusAfter([], received) == MailProcessing.Processed,
            "a message attached to nothing is not waiting on anybody");

        Console.WriteLine();
        Console.WriteLine("Except where the fetch itself failed.");
        Console.WriteLine();

        // Its status is about the fetch, not the linking. Rewriting it here
        // would hide a message SCMOS never managed to read behind a tidy word.
        Check(MailReview.StatusAfter([MailLink.Confirmed], MailProcessing.Failed) == MailProcessing.Failed,
            "a message that could not be fetched stays failed, whatever its links say");
        Check(MailReview.StatusAfter([], MailProcessing.Failed) == MailProcessing.Failed,
            "and stays failed with no links at all");

        Console.WriteLine();
        Console.WriteLine("What a person may decide.");
        Console.WriteLine();

        Check(MailReview.IsADecision(MailLink.Confirmed), "confirming is a decision");
        Check(MailReview.IsADecision(MailLink.Rejected), "so is rejecting");
        // SUGGESTED is the machine's word for "I guessed". Somebody who has
        // looked at a message has done something better than guess, even when
        // they decide it does not belong.
        Check(!MailReview.IsADecision(MailLink.Suggested),
            "a person cannot put a link back to the machine's own guess");
        Check(!MailReview.IsADecision(""), "and neither an empty status");
        Check(!MailReview.IsADecision("MAYBE"), "nor one nobody recognises");

        Console.WriteLine();
        Console.WriteLine("Which messages the badge counts.");
        Console.WriteLine();

        Check(MailReview.IsWaiting(MailProcessing.NeedReview), "one waiting on a decision is counted");
        // A message that could not be read needs somebody more than one that
        // merely has a suggestion on it.
        Check(MailReview.IsWaiting(MailProcessing.Failed), "so is one that failed to arrive properly");
        Check(!MailReview.IsWaiting(MailProcessing.Processed), "a finished one is not");
        Check(!MailReview.IsWaiting(MailProcessing.Received), "nor one the worker has not reached yet");
        Check(!MailReview.IsWaiting(MailProcessing.Processing), "nor one being worked on");

        // The badge and the list read the same two statuses. Stated as a loop so
        // adding a status to one and not the other fails here rather than on a
        // screen somebody has stopped believing.
        var agree = new[]
        {
            MailProcessing.Received, MailProcessing.Processing, MailProcessing.Processed,
            MailProcessing.NeedReview, MailProcessing.Failed,
        }.All(status => MailReview.IsWaiting(status)
            == (status == MailProcessing.NeedReview || status == MailProcessing.Failed));
        Check(agree, "and the count and the list are the same two statuses, for every status there is");

        Console.WriteLine();
        Console.WriteLine("Which view the inbox is showing.");
        Console.WriteLine();

        Check(MailReview.ViewOf("waiting") == MailReview.View.Waiting, "a view is read however it is cased");
        Check(MailReview.ViewOf("  LINKED  ") == MailReview.View.Linked, "and however it is padded");
        // A view nobody recognises must widen to everything rather than narrow
        // to nothing: an empty inbox reads as "no mail", which is a lie.
        Check(MailReview.ViewOf("nonsense") == MailReview.View.All,
            "one nobody recognises shows everything, not nothing");
        Check(MailReview.ViewOf(null) == MailReview.View.All, "and so does none at all");
        Check(MailReview.Views.Length == 4 && MailReview.Views[0] == MailReview.View.Waiting,
            "and the screen opens on what is waiting, because that is the reason to visit");

        /*
         * The screen asks for a view by name, and this decides what a name
         * means. A view the screen offers that this does not know falls back to
         * ALL — safe, but it makes the "waiting" button quietly show everything,
         * which is the worst kind of wrong: it looks like it worked.
         */
        Console.WriteLine();
        Console.WriteLine("And the screen asks for views this actually knows.");
        Console.WriteLine();

        var screen = FindInbox();
        if (screen is null)
        {
            failed++;
            Console.WriteLine("  FAIL  could not find app/scmos/mailInbox.ts to check against");
        }
        else
        {
            var listed = File.ReadAllText(screen);
            foreach (var one in MailReview.Views)
            {
                var ok = listed.Contains($"key: \"{one}\"", StringComparison.Ordinal);
                if (!ok) failed++;
                Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {one}");
                if (!ok) Console.WriteLine("        The API offers this view and the screen does not.");
            }

            // And the other way: a key on the screen that is not a view here.
            var offered = System.Text.RegularExpressions.Regex
                .Matches(listed, "key: \"([A-Z_]+)\"")
                .Select(match => match.Groups[1].Value)
                .ToList();
            var unknown = offered.Where(one => !MailReview.Views.Contains(one)).ToList();
            if (unknown.Count > 0) failed++;
            Console.WriteLine($"  {(unknown.Count == 0 ? "ok  " : "FAIL")}  "
                + "and offers none this would silently widen to everything");
            if (unknown.Count > 0)
                Console.WriteLine($"        The screen offers {string.Join(", ", unknown)}, which ViewOf does not know.");
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "All mail review checks passed."
            : $"{failed} mail review check(s) failed.");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// The inbox's own module, found by walking up from the binary. Null when
    /// this is not running inside a checkout, which the caller reports rather
    /// than skips — a check that goes quiet when it cannot find its subject is
    /// not a check.
    /// </summary>
    private static string? FindInbox()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        for (var up = 0; up < 8 && here is not null; up++, here = here.Parent)
        {
            var candidate = Path.Combine(here.FullName, "app", "scmos", "mailInbox.ts");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
