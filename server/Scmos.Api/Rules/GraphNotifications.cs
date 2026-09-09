using System.Text.Json;

namespace Scmos.Api.Rules;

/// <summary>
/// What Graph says when it calls us, and what may be believed of it.
///
/// <para>
/// The webhook is open to the internet by necessity — Microsoft's network has
/// to reach it, which means everybody's can. Nothing in a delivery is trusted
/// because it arrived; the <c>clientState</c> on each item is checked against
/// the secret stored for that subscription, and an item that fails is dropped
/// rather than answered differently. Telling an unauthenticated caller which
/// part of their guess was wrong is telling them how to get closer.
/// </para>
///
/// <para>
/// <b>A delivery is a batch.</b> Graph sends several notifications in one call,
/// each with its own subscription and its own secret, so verification is per
/// item and not per request. One bad item among four does not make the other
/// three untrustworthy, and it does not make them trustworthy either.
/// </para>
///
/// <para>
/// Pure, so <c>--check-notifications</c> can prove all of it with no Graph and
/// no subscription.
/// </para>
/// </summary>
public static class GraphNotifications
{
    /// <summary>One thing Graph is telling us about.</summary>
    /// <param name="SubscriptionId">Which subscription delivered it.</param>
    /// <param name="ClientState">The secret, to be checked against the stored one.</param>
    /// <param name="MessageId">The Graph id of the message that changed.</param>
    /// <param name="ChangeType">created · updated · deleted.</param>
    public record Notice(string SubscriptionId, string ClientState, string MessageId, string ChangeType);

    /// <summary>One thing Graph is telling us about the subscription itself.</summary>
    /// <param name="SubscriptionId">Which subscription.</param>
    /// <param name="ClientState">The secret, checked the same way.</param>
    /// <param name="Event">reauthorizationRequired · subscriptionRemoved · missed.</param>
    public record Lifecycle(string SubscriptionId, string ClientState, string Event);

    /// <summary>The lifecycle events Graph sends, spelled as Graph spells them.</summary>
    public static class Event
    {
        public const string Reauthorize = "reauthorizationRequired";
        public const string Removed = "subscriptionRemoved";

        /// <summary>
        /// Graph could not deliver something and has given up on it.
        ///
        /// The only correct response is to read the mailbox forward again.
        /// Ignoring it means mail that silently never arrived, which is the one
        /// outcome this integration cannot detect on its own.
        /// </summary>
        public const string Missed = "missed";
    }

    /// <summary>
    /// Whether a validation token is one worth echoing.
    ///
    /// <para>
    /// This endpoint answers an unauthenticated caller by repeating what they
    /// sent, which is a shape worth being careful about even when the answer is
    /// <c>text/plain</c>. Graph's token is a short opaque string, so anything
    /// long, empty, or carrying control characters is not one and is refused —
    /// the handshake it belongs to has a ten second budget and no reason to
    /// carry a kilobyte.
    /// </para>
    /// </summary>
    public static bool IsUsableToken(string? token) =>
        !string.IsNullOrEmpty(token)
        && token.Length <= 2048
        && token.All(one => one is >= ' ' and <= '~');

    /// <summary>
    /// The notifications in a delivery.
    ///
    /// An item with no subscription or no message names nothing that can be
    /// acted on, and is dropped. Everything else is returned for the caller to
    /// check the secret on — this reads, it does not decide.
    /// </summary>
    public static IReadOnlyList<Notice> ReadNotices(JsonElement root)
    {
        var found = new List<Notice>();
        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            return found;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var subscription = Text(item, "subscriptionId");
            var message = MessageIdOf(item);
            if (subscription.Length == 0 || message.Length == 0) continue;

            found.Add(new(subscription, Text(item, "clientState"), message, Text(item, "changeType")));
        }
        return found;
    }

    /// <summary>The lifecycle events in a delivery.</summary>
    public static IReadOnlyList<Lifecycle> ReadLifecycle(JsonElement root)
    {
        var found = new List<Lifecycle>();
        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            return found;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var subscription = Text(item, "subscriptionId");
            var what = Text(item, "lifecycleEvent");
            if (subscription.Length == 0 || what.Length == 0) continue;

            found.Add(new(subscription, Text(item, "clientState"), what));
        }
        return found;
    }

    /// <summary>
    /// Which message a notification is about.
    ///
    /// <para>
    /// <c>resourceData.id</c> where Graph sends it, and the last segment of
    /// <c>resource</c> where it does not — the two are the same id written
    /// twice, and depending on only the first means dropping notifications that
    /// were perfectly clear about which message they meant.
    /// </para>
    /// </summary>
    public static string MessageIdOf(JsonElement item)
    {
        if (item.TryGetProperty("resourceData", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            var id = Text(data, "id");
            if (id.Length > 0) return Fit(id);
        }

        var resource = Text(item, "resource");
        if (resource.Length == 0) return "";

        // Two spellings, both of which Graph uses:
        //   Users/{id}/Messages/{messageId}
        //   Users('{id}')/Messages('{messageId}')
        // Trimming the punctuation off the ends of the second leaves the word
        // "Messages" glued to the front of the id, which is a message id that
        // matches nothing and a notification quietly lost.
        var last = resource.TrimEnd('/').Split('/').LastOrDefault() ?? "";

        var open = last.IndexOf('(');
        var close = last.LastIndexOf(')');
        if (open >= 0 && close > open)
            last = last[(open + 1)..close];

        return Fit(last.Trim('\''));
    }

    /// <summary>Cut to the column the id is stored in, the same as everywhere else.</summary>
    private static string Fit(string id) =>
        id.Length <= Data.MailText.GraphId ? id : id[..Data.MailText.GraphId];

    private static string Text(JsonElement holder, string name) =>
        holder.ValueKind == JsonValueKind.Object
        && holder.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
            ? (found.GetString() ?? "").Trim() : "";
}
