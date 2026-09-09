using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// When a subscription is renewed, recreated or left alone, with
/// <c>--check-subscriptions</c>.
///
/// <para>
/// <b>The bug this is looking for does not throw.</b> A subscription that was
/// not renewed in time simply stops delivering, and the mailbox goes quiet —
/// which is indistinguishable from a quiet week until somebody asks why a
/// customer says they emailed. So the decision is walked against a clock here,
/// where getting it wrong is a failing line rather than three days of mail
/// nobody knows is missing.
/// </para>
/// </summary>
public static class GraphSubscriptionsCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-subscriptions")) return null;

        var failed = 0;

        void Check(bool ok, string why)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        string Due(string status, TimeSpan until) =>
            GraphSubscriptions.Due(status, now + until, now);

        Console.WriteLine();
        Console.WriteLine("What to do about a subscription, as its expiry approaches.");
        Console.WriteLine();

        Check(Due(MailSubscription.Active, TimeSpan.FromHours(70)) == GraphSubscriptions.Do.Leave,
            "a fresh one is left alone");
        Check(Due(MailSubscription.Active, TimeSpan.FromHours(25)) == GraphSubscriptions.Do.Leave,
            "and so is one an hour outside the window");
        Check(Due(MailSubscription.Active, TimeSpan.FromHours(24)) == GraphSubscriptions.Do.Renew,
            "exactly a day out, it is renewed");
        Check(Due(MailSubscription.Active, TimeSpan.FromHours(1)) == GraphSubscriptions.Do.Renew,
            "and with an hour left, still renewed");

        // Renewing an expired subscription is not something Graph offers.
        Check(Due(MailSubscription.Active, TimeSpan.Zero) == GraphSubscriptions.Do.Recreate,
            "at the moment it expires it is made again, not renewed");
        Check(Due(MailSubscription.Active, TimeSpan.FromHours(-1)) == GraphSubscriptions.Do.Recreate,
            "and once past, certainly made again");

        Console.WriteLine();
        Console.WriteLine("And the clock is believed over the row.");
        Console.WriteLine();

        // The ordinary case, and the one worth stating: Graph never tells
        // anybody it has stopped, so a row goes on claiming to be ACTIVE long
        // after it is not. Believing the status here is how a mailbox goes
        // quiet for three days.
        Check(Due(MailSubscription.Active, TimeSpan.FromDays(-2)) == GraphSubscriptions.Do.Recreate,
            "a row still marked ACTIVE whose time has passed is not active");
        Check(Due(MailSubscription.Expired, TimeSpan.FromHours(70)) == GraphSubscriptions.Do.Recreate,
            "one Graph has expired is made again, however long it claims to have left");
        Check(Due(MailSubscription.Failed, TimeSpan.FromHours(70)) == GraphSubscriptions.Do.Recreate,
            "so is one that failed");
        Check(Due(MailSubscription.Deleted, TimeSpan.FromHours(70)) == GraphSubscriptions.Do.Recreate,
            "and one that was deleted");
        Check(Due("", TimeSpan.FromHours(70)) == GraphSubscriptions.Do.Recreate,
            "a status nobody recognises is not treated as working");

        Console.WriteLine();
        Console.WriteLine("How long it is asked to live.");
        Console.WriteLine();

        var asked = GraphSubscriptions.ExpiryFrom(now) - now;
        // Graph caps Outlook resources at 4,230 minutes and refuses the request
        // outright above it, rather than quietly giving less.
        Check(asked.TotalMinutes < 4230, "less than Graph's cap, so the request is not refused");
        Check(asked > GraphSubscriptions.RenewWithin,
            "and longer than the renewal window, or every subscription would be born due");
        Check(asked.TotalHours > 48, "comfortably more than two days");

        Console.WriteLine();
        Console.WriteLine("What the subscription watches.");
        Console.WriteLine();

        // The id is preferred because an address can be reassigned and an id
        // cannot — a renewal against the address alone would follow the name to
        // whoever holds it next.
        Check(GraphSubscriptions.ResourceFor("abc-123", "ops@leschaco.co.th", "")
            == "users/abc-123/messages", "the Graph user id is used when it is known");
        Check(GraphSubscriptions.ResourceFor("", "OPS@Leschaco.co.th", "")
            == "users/ops@leschaco.co.th/messages", "the address is the fallback, spelled one way");
        Check(GraphSubscriptions.ResourceFor("abc-123", "ops@leschaco.co.th", "inbox")
            == "users/abc-123/mailFolders/inbox/messages", "a folder narrows it");
        Check(GraphSubscriptions.ResourceFor("", "", "") == "",
            "and a row naming neither watches nothing, rather than watching everything");

        Console.WriteLine();
        Console.WriteLine("The secret Graph echoes back.");
        Console.WriteLine();

        var secret = GraphSubscriptions.NewClientState();
        Check(secret.Length >= 40, "long enough to be unguessable");
        Check(secret.Length <= 200, "and short enough for the column it is stored in");
        Check(secret != GraphSubscriptions.NewClientState(), "two are never the same");
        // One per subscription, so a leak from one mailbox does not
        // authenticate deliveries for another.
        Check(new HashSet<string>(Enumerable.Range(0, 200)
            .Select(_ => GraphSubscriptions.NewClientState())).Count == 200,
            "two hundred of them are two hundred different secrets");
        Check(secret.All(one => char.IsAsciiLetterOrDigit(one) || one is '-' or '_'),
            "and it survives a URL and a JSON body without escaping");

        Check(GraphSubscriptions.StateMatches(secret, secret), "the right secret matches");
        Check(!GraphSubscriptions.StateMatches(secret, secret + "x"), "a longer one does not");
        Check(!GraphSubscriptions.StateMatches(secret, secret[..^1]), "nor a shorter one");
        // A subscription with no secret must never accept anything. The webhook
        // is reachable from Microsoft's network, which means from everybody's.
        Check(!GraphSubscriptions.StateMatches("", ""), "no stored secret accepts nothing, not everything");
        Check(!GraphSubscriptions.StateMatches(null, secret), "and neither does a missing one");
        Check(!GraphSubscriptions.StateMatches(secret, null), "a delivery carrying no secret is refused");

        Console.WriteLine();
        Console.WriteLine("Where Graph is willing to deliver.");
        Console.WriteLine();

        Check(GraphSubscriptions.IsDeliverable("https://scmos-api.azurewebsites.net/api/x"),
            "a public HTTPS address is deliverable");
        Check(!GraphSubscriptions.IsDeliverable("http://scmos-api.azurewebsites.net/api/x"),
            "plain HTTP is not — Graph refuses it");

        // The setting nobody changed. Graph's own refusal is a generic
        // validation error that does not mention the reason.
        Check(!GraphSubscriptions.IsDeliverable("https://localhost:5199/api/x"),
            "localhost is a value somebody forgot to change, not a webhook");
        Check(!GraphSubscriptions.IsDeliverable("https://127.0.0.1/api/x"), "so is a loopback address");
        Check(!GraphSubscriptions.IsDeliverable("https://10.0.0.4/api/x"), "and a private one");
        Check(!GraphSubscriptions.IsDeliverable("https://192.168.1.10/api/x"), "and another private one");
        Check(!GraphSubscriptions.IsDeliverable("https://172.16.0.9/api/x"), "and the range people forget");
        Check(GraphSubscriptions.IsDeliverable("https://172.32.0.9/api/x"),
            "but 172.32 is public, and is not refused by a rule that was too rough");
        Check(!GraphSubscriptions.IsDeliverable("https://user:pass@example.com/api/x"),
            "credentials in the URL are refused");
        Check(!GraphSubscriptions.IsDeliverable(""), "an empty setting is not a webhook");
        Check(!GraphSubscriptions.IsDeliverable(null), "and neither is none at all");

        Console.WriteLine();
        Console.WriteLine("Where the webhook will be.");
        Console.WriteLine();

        // The paths are constants rather than app settings, so the value that
        // reaches Graph cannot be a path somebody typed into a portal wrongly.
        Check(GraphSubscriptionService.NotifyPath.StartsWith('/')
            && GraphSubscriptionService.LifecyclePath.StartsWith('/'), "both paths are rooted");
        Check(GraphSubscriptionService.NotifyPath != GraphSubscriptionService.LifecyclePath,
            "and they are two different endpoints — a lifecycle event is not a notification");

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "All Graph subscription checks passed."
            : $"{failed} Graph subscription check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
