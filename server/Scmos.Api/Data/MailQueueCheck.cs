using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// What the mail worker does when a fetch goes wrong, with
/// <c>--check-queue</c>.
///
/// <para>
/// The decision worth checking is not success. It is which of three very
/// different faults a failure was: a message that is gone and will never
/// arrive, Graph having a bad minute, or a permission nobody has granted.
/// Getting the third one wrong is the expensive mistake — it burns the retry
/// budget of every message in the queue against a configuration job that takes
/// five minutes, and turns switching the integration on into re-queueing a
/// morning's mail.
/// </para>
/// </summary>
public static class MailQueueCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-queue")) return null;

        var failed = 0;

        void Check(bool ok, string why)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        string Decide(string code, int retries = 0) => MailQueue.Decide(code, false, retries);

        Console.WriteLine();
        Console.WriteLine("Whose fault a failed fetch was.");
        Console.WriteLine();

        Check(MailQueue.Decide(GraphDiagnosis.Code.Connected, true, 0) == MailQueue.Next.Store,
            "a message that arrived is stored");

        // Deleted or moved between the notification and the fetch. Retrying it
        // three times and then listing it as failed puts a permanent entry on
        // somebody's list of things to fix, for something nobody did wrong.
        Check(Decide(GraphDiagnosis.Code.NotFound) == MailQueue.Next.Gone,
            "a message that no longer exists finishes the row rather than retrying");

        Console.WriteLine();
        Console.WriteLine("The faults that are true of every row, not this one.");
        Console.WriteLine();

        Check(Decide(GraphDiagnosis.Code.NoConsent) == MailQueue.Next.Pause,
            "a missing consent stops the pass, it does not fail the message");
        Check(Decide(GraphDiagnosis.Code.NotScoped) == MailQueue.Next.Pause,
            "so does a scope that does not cover the mailbox");
        Check(Decide(GraphDiagnosis.Code.NoToken) == MailQueue.Next.Pause, "so does no token");
        Check(Decide(GraphDiagnosis.Code.NotApproved) == MailQueue.Next.Pause,
            "so does a mailbox this deployment never approved");
        Check(Decide(GraphDiagnosis.Code.Rejected) == MailQueue.Next.Pause,
            "so does an identity Graph will not accept");
        // Continuing is what makes throttling worse. Graph is asking to be left
        // alone, not reporting a fault with this message.
        Check(Decide(GraphDiagnosis.Code.Throttled) == MailQueue.Next.Pause,
            "and throttling stops the pass rather than spending a retry on it");

        // The expensive mistake, stated as its own line: none of the above may
        // ever consume an attempt, however many times the worker meets them.
        var neverSpendsRetries = new[]
        {
            GraphDiagnosis.Code.NoConsent, GraphDiagnosis.Code.NotScoped, GraphDiagnosis.Code.NoToken,
            GraphDiagnosis.Code.NotApproved, GraphDiagnosis.Code.Rejected, GraphDiagnosis.Code.Throttled,
        }.All(code => Decide(code, MailQueue.MaxRetries + 5) == MailQueue.Next.Pause);
        Check(neverSpendsRetries,
            "and none of them ever becomes a failure, however many attempts the row has spent");

        Console.WriteLine();
        Console.WriteLine("And the ones worth trying again.");
        Console.WriteLine();

        Check(Decide(GraphDiagnosis.Code.GraphError) == MailQueue.Next.Retry,
            "Graph having a bad minute is retried");
        Check(Decide(GraphDiagnosis.Code.Unexpected) == MailQueue.Next.Retry,
            "and so is an answer nobody anticipated");
        Check(Decide(GraphDiagnosis.Code.GraphError, MailQueue.MaxRetries - 2) == MailQueue.Next.Retry,
            "one attempt short of the limit, still retried");
        Check(Decide(GraphDiagnosis.Code.GraphError, MailQueue.MaxRetries - 1) == MailQueue.Next.GiveUp,
            $"and at {MailQueue.MaxRetries} attempts it is left for a person");
        Check(Decide(GraphDiagnosis.Code.GraphError, MailQueue.MaxRetries + 9) == MailQueue.Next.GiveUp,
            "and stays that way rather than wrapping round");

        Console.WriteLine();
        Console.WriteLine("What stops a pass, asked before there is a row.");
        Console.WriteLine();

        Check(MailQueue.Halts(GraphDiagnosis.Code.NoConsent, false), "a consent fault halts a catch-up");
        Check(MailQueue.Halts(GraphDiagnosis.Code.Throttled, false), "so does throttling");
        Check(!MailQueue.Halts(GraphDiagnosis.Code.GraphError, false), "a bad minute does not");
        Check(!MailQueue.Halts(GraphDiagnosis.Code.NotFound, false), "nor does a message that is gone");
        Check(!MailQueue.Halts(GraphDiagnosis.Code.Connected, true), "and success certainly does not");

        Console.WriteLine();
        Console.WriteLine("Where a mailbox is read from.");
        Console.WriteLine();

        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var mark = now.AddHours(-3);
        Check(MailQueue.ReadFrom(mark, now) == mark, "the high-water mark, where there is one");

        // A shared mailbox at a forwarding company holds years. Importing all of
        // it because somebody switched the integration on is not a decision the
        // integration gets to make.
        var first = MailQueue.ReadFrom(null, now);
        Check(first == now - MailQueue.FirstReadWindow,
            "and a bounded window on the first read, not the whole history");
        Check(first < now && MailQueue.FirstReadWindow > TimeSpan.FromDays(1),
            "wide enough to be useful, narrow enough to finish");

        Console.WriteLine();
        Console.WriteLine("The claim, and giving up on it.");
        Console.WriteLine();

        // Measured from the claim, never from arrival. A backlog is full of rows
        // that arrived long ago and are being worked on right now.
        Check(MailQueue.Abandoned > TimeSpan.FromMinutes(1),
            "an abandoned claim is longer than any pass takes");
        Check(MailQueue.Abandoned < TimeSpan.FromHours(1),
            "and shorter than anybody would wait to notice");
        Check(MailQueue.MaxRetries >= 2 && MailQueue.MaxRetries <= 5,
            "and a message is tried a few times, not once and not forever");

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "All mail queue checks passed."
            : $"{failed} mail queue check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
