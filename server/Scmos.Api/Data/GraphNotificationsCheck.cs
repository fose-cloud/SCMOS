using System.Text.Json;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// Reading what Graph delivers, with <c>--check-notifications</c>.
///
/// <para>
/// This endpoint is open to the internet by necessity — Microsoft's network has
/// to reach it, so everybody's can. What can be proved here with no Graph and
/// no subscription is what the parser believes and what the secret comparison
/// accepts, and both of those are the whole of the endpoint's authentication.
/// </para>
/// </summary>
public static class GraphNotificationsCheck
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-notifications")) return null;

        var failed = 0;

        void Check(bool ok, string why)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        Console.WriteLine();
        Console.WriteLine("A delivery of messages.");
        Console.WriteLine();

        var delivery = GraphNotifications.ReadNotices(Json("""
        {
          "value": [
            {
              "subscriptionId": "sub-1",
              "changeType": "created",
              "clientState": "secret-one",
              "resource": "Users/abc/Messages/AAMkONE=",
              "resourceData": { "@odata.type": "#Microsoft.Graph.Message", "id": "AAMkONE=" }
            },
            {
              "subscriptionId": "sub-1",
              "changeType": "created",
              "clientState": "secret-one",
              "resource": "Users/abc/Messages/AAMkTWO=",
              "resourceData": { "id": "AAMkTWO=" }
            }
          ]
        }
        """));

        // Graph batches. Verification is per item, not per request.
        Check(delivery.Count == 2, "one call can carry several notifications");
        Check(delivery[0].MessageId == "AAMkONE=" && delivery[1].MessageId == "AAMkTWO=",
            "and each names its own message");
        Check(delivery.All(one => one.ClientState == "secret-one"),
            "each carries the secret it is to be checked against");
        Check(delivery[0].ChangeType == "created", "and what happened to it");

        Console.WriteLine();
        Console.WriteLine("Which message it means.");
        Console.WriteLine();

        Check(GraphNotifications.MessageIdOf(Json("""
        { "resourceData": { "id": "FROM-DATA" }, "resource": "Users/abc/Messages/FROM-PATH" }
        """)) == "FROM-DATA", "resourceData.id is preferred where Graph sends it");

        // The same id written twice. Depending only on the first would drop
        // notifications that were perfectly clear about what they meant.
        Check(GraphNotifications.MessageIdOf(Json("""
        { "resource": "Users/abc/Messages/FROM-PATH" }
        """)) == "FROM-PATH", "and the resource path is read when it does not");

        Check(GraphNotifications.MessageIdOf(Json("""
        { "resource": "Users/abc/Messages/FROM-PATH/" }
        """)) == "FROM-PATH", "a trailing slash does not become the id");
        Check(GraphNotifications.MessageIdOf(Json("""
        { "resource": "Users('abc')/Messages('QUOTED')" }
        """)) == "QUOTED", "nor do the quotes an OData path puts round it");
        Check(GraphNotifications.MessageIdOf(Json("""{ }""")) == "",
            "and an item naming nothing yields nothing");

        var oversized = GraphNotifications.MessageIdOf(Json($$"""
        { "resourceData": { "id": "{{new string('i', MailText.GraphId + 100)}}" } }
        """));
        Check(oversized.Length == MailText.GraphId,
            $"an id past {MailText.GraphId} is cut to the column, like everywhere else");

        Console.WriteLine();
        Console.WriteLine("What is dropped before anything looks at it.");
        Console.WriteLine();

        var partial = GraphNotifications.ReadNotices(Json("""
        {
          "value": [
            { "subscriptionId": "sub-1", "resourceData": { "id": "GOOD" } },
            { "resourceData": { "id": "NO SUBSCRIPTION" } },
            { "subscriptionId": "sub-2" },
            "not an object"
          ]
        }
        """));
        Check(partial.Count == 1, "an item with no subscription or no message names nothing actionable");
        Check(partial[0].MessageId == "GOOD", "and the one that does survives");

        Check(GraphNotifications.ReadNotices(Json("""{ }""")).Count == 0,
            "a body with no value is no notifications, not an exception");
        Check(GraphNotifications.ReadNotices(Json("""{ "value": "text" }""")).Count == 0,
            "and neither is a value that is not a list");

        Console.WriteLine();
        Console.WriteLine("The subscription itself being in trouble.");
        Console.WriteLine();

        var lifecycle = GraphNotifications.ReadLifecycle(Json("""
        {
          "value": [
            { "subscriptionId": "sub-1", "lifecycleEvent": "reauthorizationRequired", "clientState": "s" },
            { "subscriptionId": "sub-2", "lifecycleEvent": "subscriptionRemoved", "clientState": "s" },
            { "subscriptionId": "sub-3", "lifecycleEvent": "missed", "clientState": "s" },
            { "subscriptionId": "sub-4" }
          ]
        }
        """));
        Check(lifecycle.Count == 3, "three events read, and one with no event dropped");
        Check(lifecycle[0].Event == GraphNotifications.Event.Reauthorize
            && lifecycle[1].Event == GraphNotifications.Event.Removed
            && lifecycle[2].Event == GraphNotifications.Event.Missed,
            "each spelled the way Graph spells it, so the switch actually matches");

        // Spelling these wrong is a silent failure: the switch falls through,
        // nothing acts, and a dead subscription is never remade.
        Check(GraphNotifications.Event.Reauthorize == "reauthorizationRequired"
            && GraphNotifications.Event.Removed == "subscriptionRemoved"
            && GraphNotifications.Event.Missed == "missed",
            "and the constants are Graph's own spellings, not near-misses");

        Console.WriteLine();
        Console.WriteLine("The handshake token.");
        Console.WriteLine();

        // This endpoint answers an unauthenticated caller by repeating their
        // own input. The smallest version of that is the safest one.
        Check(GraphNotifications.IsUsableToken("Validation: Testing client application reachability"),
            "the token Graph actually sends is echoed");
        Check(!GraphNotifications.IsUsableToken(""), "an empty one is not");
        Check(!GraphNotifications.IsUsableToken(null), "nor a missing one");
        Check(!GraphNotifications.IsUsableToken(new string('t', 2049)), "nor a kilobyte of it");
        Check(!GraphNotifications.IsUsableToken("has\nnewline"), "nor one carrying control characters");
        Check(!GraphNotifications.IsUsableToken("has\0null"), "nor one carrying a null");
        Check(GraphNotifications.IsUsableToken(new string('t', 2048)), "but exactly the limit is fine");

        Console.WriteLine();
        Console.WriteLine("The secret, which is the whole of the authentication.");
        Console.WriteLine();

        var stored = GraphSubscriptions.NewClientState();
        Check(GraphSubscriptions.StateMatches(stored, stored), "a genuine delivery is accepted");
        Check(!GraphSubscriptions.StateMatches(stored, "guess"), "a guess is not");
        // A subscription row written without a secret would otherwise accept
        // every delivery from anybody, on an endpoint anybody can call.
        Check(!GraphSubscriptions.StateMatches("", ""), "and an empty stored secret accepts nothing");

        Console.WriteLine();
        Console.WriteLine("Where Graph is told to deliver, and where this API listens.");
        Console.WriteLine();

        // The route is mapped at these constants themselves, so the URL handed
        // to Graph and the path served cannot drift into a 404 nobody sees.
        Check(GraphSubscriptionService.NotifyPath == "/api/integrations/graph/notify",
            "the notification path is the one written in the setup notes");
        Check(GraphSubscriptionService.LifecyclePath == "/api/integrations/graph/lifecycle",
            "and so is the lifecycle path");
        Check(GraphSubscriptionService.NotifyPath != GraphSubscriptionService.LifecyclePath,
            "and they are two endpoints, because a lifecycle event is not a notification");

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "All Graph notification checks passed."
            : $"{failed} Graph notification check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
