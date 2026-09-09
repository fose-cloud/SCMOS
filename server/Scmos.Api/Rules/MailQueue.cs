namespace Scmos.Api.Rules;

/// <summary>
/// What the mail worker does with a row when a fetch does not go as planned.
///
/// <para>
/// The interesting decision is not "did it work". It is <b>whose fault it is</b>
/// — because the answers pull in opposite directions. A message that has been
/// deleted will never arrive however often it is asked for, so retrying it
/// three times and marking it failed puts a permanent entry on a list of things
/// somebody is supposed to fix. A missing consent affects every row in the
/// queue equally, so a worker that keeps going burns the retry budget of a
/// thousand messages against a permission nobody has granted yet, and turns a
/// five minute configuration job into a morning of re-queueing.
/// </para>
///
/// <para>
/// So a configuration fault stops the pass rather than failing the row, and a
/// message that is gone finishes the row rather than retrying it. Only genuine
/// transient trouble — Graph having a bad minute — spends a retry.
/// </para>
/// </summary>
public static class MailQueue
{
    /// <summary>
    /// How many times a message is retried before it is left for a person.
    ///
    /// The same three as the LINE queue. A fourth attempt against something
    /// that has failed three times is not a different attempt.
    /// </summary>
    public const int MaxRetries = 3;

    /// <summary>
    /// How long a claimed row may sit before it is assumed abandoned.
    ///
    /// Longer than any pass takes and far shorter than anybody would wait.
    /// Measured from the <b>claim</b>, never from arrival: a backlog is full of
    /// rows that arrived long ago and are being worked on right now, and
    /// testing arrival would hand those to a second instance while the first
    /// still held them.
    /// </summary>
    public static readonly TimeSpan Abandoned = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How far back a mailbox is read the first time, when nothing has been
    /// read from it before.
    ///
    /// <para>
    /// Seven days rather than everything. A shared mailbox at a forwarding
    /// company holds years, and a first connection that tried to import all of
    /// it would spend a day paging through Graph, mostly over mail about
    /// shipments that were delivered in 2023. Moving the history in is a
    /// deliberate job somebody asks for, not a side effect of switching the
    /// integration on.
    /// </para>
    /// </summary>
    public static readonly TimeSpan FirstReadWindow = TimeSpan.FromDays(7);

    /// <summary>What to do with the row.</summary>
    public static class Next
    {
        /// <summary>It arrived. Store it and finish.</summary>
        public const string Store = "STORE";

        /// <summary>The message no longer exists. Finish the row; asking again cannot help.</summary>
        public const string Gone = "GONE";

        /// <summary>Graph had a bad minute. Put it back and count an attempt.</summary>
        public const string Retry = "RETRY";

        /// <summary>It has failed enough times. Leave it for a person.</summary>
        public const string GiveUp = "GIVE_UP";

        /// <summary>
        /// Nothing is wrong with this row. Stop the pass and change nothing.
        ///
        /// For faults that apply to every row equally — a missing consent, a
        /// scope that does not cover the mailbox, throttling. Spending this
        /// row's retries on them would be spending every row's.
        /// </summary>
        public const string Pause = "PAUSE";
    }

    /// <summary>
    /// What to do about one fetch.
    /// </summary>
    /// <param name="code">A <see cref="GraphDiagnosis.Code"/>.</param>
    /// <param name="ok">Whether the fetch succeeded.</param>
    /// <param name="retries">How many attempts this row has already spent.</param>
    public static string Decide(string? code, bool ok, int retries)
    {
        if (ok) return Next.Store;

        return code switch
        {
            // Deleted, or moved out of the folder being watched, between the
            // notification and the fetch. Ordinary, and not a fault.
            GraphDiagnosis.Code.NotFound => Next.Gone,

            // Every one of these is true of the whole queue, not of this row.
            GraphDiagnosis.Code.NoToken => Next.Pause,
            GraphDiagnosis.Code.NoConsent => Next.Pause,
            GraphDiagnosis.Code.NotScoped => Next.Pause,
            GraphDiagnosis.Code.NotApproved => Next.Pause,
            GraphDiagnosis.Code.Rejected => Next.Pause,
            // Throttling especially: continuing is what makes it worse, and
            // Graph is asking to be left alone rather than reporting a fault.
            GraphDiagnosis.Code.Throttled => Next.Pause,

            // Anything else is Graph having a bad minute, or an answer nobody
            // anticipated. Both are worth trying again, and both are worth
            // giving up on eventually.
            _ => retries + 1 >= MaxRetries ? Next.GiveUp : Next.Retry,
        };
    }

    /// <summary>
    /// Whether a fault stops the pass. The same question as
    /// <see cref="Decide"/> asking for <see cref="Next.Pause"/>, asked before
    /// there is a row to decide about.
    /// </summary>
    public static bool Halts(string? code, bool ok) =>
        !ok && Decide(code, false, 0) == Next.Pause;

    /// <summary>
    /// Where to start reading a mailbox that has never been read.
    ///
    /// The high-water mark where there is one, and a bounded window where there
    /// is not — see <see cref="FirstReadWindow"/>.
    /// </summary>
    public static DateTimeOffset ReadFrom(DateTimeOffset? lastSynced, DateTimeOffset now) =>
        lastSynced ?? now - FirstReadWindow;
}
