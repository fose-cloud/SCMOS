using System.Globalization;
using System.Text.Json;
using Scmos.Api.Data;

namespace Scmos.Api.Rules;

/// <summary>
/// Turning what Graph sends into what SCMOS stores.
///
/// <para>
/// The reading half of the mail pipeline, and the half where the bugs are. A
/// message from a shipping line is not the tidy object the documentation shows:
/// the sender can be absent, a recipient list can be empty or hold the same
/// address twice, a forwarded thread's subject carries every previous subject
/// and runs past any column, and a signature image arrives as an attachment
/// that is not a document.
/// </para>
///
/// <para>
/// Pure and separate from the calls that fetch it, so <c>--check-messages</c>
/// can prove all of that against fixed payloads with no mailbox, no token and
/// no database. Nothing here saves anything — persistence and deduplication are
/// their own step, and a parser that writes is a parser that cannot be run
/// twice on the same input to see whether it agrees with itself.
/// </para>
///
/// <para>
/// <b>Everything is cut to the width of the column it goes in.</b> Not for
/// tidiness: a value one character too long does not fail here, it fails at
/// <c>SaveChanges</c>, after the message has been fetched and the work done,
/// and it fails on the day somebody sends a long enough subject line rather
/// than on the day the code was written. The widths are
/// <see cref="MailText"/>'s, which the model reads too.
/// </para>
/// </summary>
public static class GraphMessages
{
    /// <summary>
    /// One message and everybody on it, ready for a mailbox id and a save.
    /// </summary>
    /// <param name="Message">The message. <c>MailboxId</c> is the caller's to set.</param>
    /// <param name="Participants">To, Cc and Bcc, one row each, deduplicated.</param>
    public record Read(Email Message, IReadOnlyList<EmailParticipant> Participants);

    /// <summary>A page of messages, and where the rest of them are.</summary>
    /// <param name="Messages">What this page held, in the order Graph returned it.</param>
    /// <param name="NextLink">Graph's <c>@odata.nextLink</c>, or empty when this was the last page.</param>
    public record Page(IReadOnlyList<Read> Messages, string NextLink);

    /// <summary>
    /// The fields worth asking Graph for.
    ///
    /// Named rather than taking the default, because the default includes the
    /// body of every message in a list — and a page of fifty threads with their
    /// quoted history is a response measured in megabytes for the sake of the
    /// four fields a queue actually needs.
    /// </summary>
    public const string Fields =
        "id,conversationId,internetMessageId,subject,from,toRecipients,ccRecipients,bccRecipients,"
        + "sentDateTime,receivedDateTime,hasAttachments,body";

    /// <summary>
    /// A page of messages from a list response.
    ///
    /// A message that cannot be read is skipped rather than failing the page.
    /// One malformed item in fifty should cost one message, not the other
    /// forty-nine — and the ones that are skipped are counted by the caller, so
    /// silence is not the same as success.
    /// </summary>
    public static Page ReadPage(JsonElement root)
    {
        var messages = new List<Read>();
        if (root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                if (ReadOne(item) is { } one) messages.Add(one);

        return new(messages, Text(root, "@odata.nextLink", 2048));
    }

    /// <summary>
    /// One message, or null when it carries no id.
    ///
    /// The id is the only field there is no sensible default for: it is what
    /// makes a redelivered notification the same message rather than a second
    /// one, and a message stored without it would be stored again on every
    /// delivery.
    /// </summary>
    public static Read? ReadOne(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;

        var id = Text(item, "id", MailText.GraphId);
        if (id.Length == 0) return null;

        var (fromName, fromAddress) = Person(item.TryGetProperty("from", out var from) ? from : default);

        var received = Moment(item, "receivedDateTime");
        var message = new Email
        {
            GraphMessageId = id,
            ConversationId = Text(item, "conversationId", MailText.GraphId),
            InternetMessageId = Text(item, "internetMessageId", MailText.GraphId),
            Subject = Text(item, "subject", MailText.Subject),
            FromAddress = fromAddress,
            FromName = fromName,
            SentAt = Moment(item, "sentDateTime") ?? received ?? default,
            // A message with no received time would sort to the front of the
            // queue forever. Falling back to when it was sent is the closest
            // true thing; falling back to now would make an old message look new.
            ReceivedAt = received ?? Moment(item, "sentDateTime") ?? default,
            HasAttachments = Flag(item, "hasAttachments"),
            ProcessingStatus = MailProcessing.Received,
        };

        // Which body is which is Graph's to say, and it says so. Asking for
        // text and storing it as HTML would put escaped markup in front of a
        // person; the other way round would hand the extractor tags to read
        // container numbers out of.
        if (item.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object)
        {
            var content = Text(body, "content", int.MaxValue);
            if (string.Equals(Text(body, "contentType", 16), "html", StringComparison.OrdinalIgnoreCase))
                message.BodyHtml = content;
            else
                message.BodyText = content;
        }

        return new(message, Participants(item));
    }

    /// <summary>
    /// Everybody the message was addressed to, one row each and no repeats.
    ///
    /// <para>
    /// Deduplicated on the kind and the address together, so somebody who
    /// appears twice in <c>To</c> is one row while somebody in both <c>To</c>
    /// and <c>Cc</c> is two — which is what happened, and the screen should be
    /// able to say so.
    /// </para>
    /// </summary>
    public static IReadOnlyList<EmailParticipant> Participants(JsonElement item)
    {
        var found = new List<EmailParticipant>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Take(string field, string kind)
        {
            if (!item.TryGetProperty(field, out var list) || list.ValueKind != JsonValueKind.Array) return;
            foreach (var entry in list.EnumerateArray())
            {
                var (name, address) = Person(entry);
                // An entry with no address is a name nothing can be sent to and
                // nothing can be matched on — the table exists to answer "which
                // messages went to this customer".
                if (address.Length == 0) continue;
                if (!seen.Add($"{kind} {address}")) continue;
                found.Add(new EmailParticipant { Kind = kind, Address = address, DisplayName = name });
            }
        }

        Take("toRecipients", MailParticipant.To);
        Take("ccRecipients", MailParticipant.Cc);
        Take("bccRecipients", MailParticipant.Bcc);
        return found;
    }

    /// <summary>
    /// The files worth keeping, from an attachment list response.
    ///
    /// <para>
    /// <b>Inline attachments are dropped.</b> Every signature block in the
    /// industry carries a logo, and a company footer with three images would
    /// otherwise put three files into Blob for every message that arrives —
    /// thousands of copies of the same logo, none of them a document anybody
    /// asked to keep.
    /// </para>
    ///
    /// <para>
    /// <c>StoredDocumentId</c> stays zero. The bytes are fetched separately, and
    /// a row with no document yet is the ordinary state rather than a fault.
    /// </para>
    /// </summary>
    public static IReadOnlyList<EmailAttachment> ReadAttachments(JsonElement root)
    {
        var found = new List<EmailAttachment>();
        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            return found;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (Flag(item, "isInline")) continue;

            var id = Text(item, "id", MailText.GraphId);
            if (id.Length == 0 || !seen.Add(id)) continue;

            found.Add(new EmailAttachment
            {
                GraphAttachmentId = id,
                FileName = Text(item, "name", MailText.FileName),
                ContentType = Text(item, "contentType", MailText.ContentType),
                SizeBytes = item.TryGetProperty("size", out var size)
                    && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var bytes)
                        ? bytes : 0,
            });
        }
        return found;
    }

    /// <summary>
    /// Whether a paging link may be followed.
    ///
    /// <para>
    /// <c>@odata.nextLink</c> is a URL taken out of a response body and then
    /// requested <b>with the bearer token attached</b>. Following it without
    /// looking is how a token ends up at whatever host the link happens to
    /// name. This response comes from Graph over TLS so it is not attacker
    /// controlled today, and the check costs nothing — the version of this that
    /// gets exploited is always the one where somebody reasoned it was safe.
    /// </para>
    ///
    /// <para>
    /// Strict about the host on purpose. A national-cloud deployment reaches
    /// Graph at a different name, and it would change
    /// <c>GraphAuth.Endpoint</c> too — so it should fail here loudly rather
    /// than be quietly allowed by a looser rule.
    /// </para>
    /// </summary>
    public static bool IsGraphLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var link)
        && link.Scheme == Uri.UriSchemeHttps
        && string.Equals(link.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(link.UserInfo);

    /// <summary>A name and an address out of Graph's nested emailAddress object.</summary>
    private static (string Name, string Address) Person(JsonElement holder)
    {
        if (holder.ValueKind != JsonValueKind.Object
            || !holder.TryGetProperty("emailAddress", out var who)
            || who.ValueKind != JsonValueKind.Object) return ("", "");

        // Lower-cased and trimmed here, the same way GraphMailboxes spells an
        // address, so a match on a participant is a match however the sending
        // client capitalised it.
        return (Text(who, "name", MailText.PersonName),
            Text(who, "address", MailText.Address).ToLowerInvariant());
    }

    /// <summary>A string field, trimmed and cut to the column it belongs in.</summary>
    private static string Text(JsonElement holder, string name, int width)
    {
        if (holder.ValueKind != JsonValueKind.Object
            || !holder.TryGetProperty(name, out var found)
            || found.ValueKind != JsonValueKind.String) return "";

        var value = (found.GetString() ?? "").Trim();
        return value.Length <= width ? value : value[..width];
    }

    /// <summary>A boolean field. Anything that is not true is false.</summary>
    private static bool Flag(JsonElement holder, string name) =>
        holder.ValueKind == JsonValueKind.Object
        && holder.TryGetProperty(name, out var found)
        && found.ValueKind == JsonValueKind.True;

    /// <summary>
    /// A timestamp, or null when Graph did not give a readable one.
    ///
    /// Round-tripped rather than parsed loosely: Graph writes UTC and the
    /// register runs on Thai time, and a date read in the server's own zone is
    /// the bug that makes an evening message land on the wrong day.
    /// </summary>
    private static DateTimeOffset? Moment(JsonElement holder, string name)
    {
        if (holder.ValueKind != JsonValueKind.Object
            || !holder.TryGetProperty(name, out var found)
            || found.ValueKind != JsonValueKind.String) return null;

        return DateTimeOffset.TryParse(found.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var moment) ? moment : null;
    }
}
