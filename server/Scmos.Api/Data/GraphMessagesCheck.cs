using System.Text.Json;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Reading a Graph payload into rows, with <c>--check-messages</c>.
///
/// <para>
/// The payloads below are the shapes Graph actually sends, including the ones
/// its documentation does not dwell on: a message with no sender, a recipient
/// list holding the same address twice, a forwarded subject longer than the
/// column it goes in, and a signature logo arriving as an attachment. No
/// mailbox, no token, no database — which is the point, because this is the
/// half of message retrieval that can be got right before the Entra grant
/// exists.
/// </para>
/// </summary>
public static class GraphMessagesCheck
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-messages")) return null;

        var failed = 0;

        void Check(bool ok, string why)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        Console.WriteLine();
        Console.WriteLine("One ordinary message from a shipping line.");
        Console.WriteLine();

        var ordinary = Json("""
        {
          "id": "AAMkAGI2THVSAAA=",
          "conversationId": "AAQkAGI2THVS",
          "internetMessageId": "<abc123@evergreen-line.com>",
          "subject": "ARRIVAL NOTICE / TEMU0404097 / BKKGG8920900",
          "from": { "emailAddress": { "name": "Evergreen BKK", "address": "Docs@Evergreen-Line.COM" } },
          "toRecipients": [ { "emailAddress": { "name": "SCMOS Ops", "address": "ops@leschaco.co.th" } } ],
          "ccRecipients": [ { "emailAddress": { "name": "Import", "address": "import@leschaco.co.th" } } ],
          "sentDateTime": "2026-09-08T17:04:06Z",
          "receivedDateTime": "2026-09-08T17:04:11Z",
          "hasAttachments": true,
          "body": { "contentType": "text", "content": "Container TEMU0404097 has arrived." }
        }
        """);

        var read = GraphMessages.ReadOne(ordinary);
        Check(read is not null, "it reads at all");

        if (read is null)
        {
            Console.WriteLine("  (the rest of this section cannot run)");
        }
        else
        {
            Check(read.Message.GraphMessageId == "AAMkAGI2THVSAAA=", "the id is kept exactly");
            Check(read.Message.Subject.StartsWith("ARRIVAL NOTICE"), "the subject is kept");
            Check(read.Message.FromName == "Evergreen BKK", "the sender's name is kept");
            // Lower-cased, so the same sender matches however their client wrote it.
            Check(read.Message.FromAddress == "docs@evergreen-line.com",
                "and their address is lower-cased, however they capitalised it");
            Check(read.Message.HasAttachments, "the attachment flag is carried");
            Check(read.Message.BodyText.Contains("TEMU0404097") && read.Message.BodyHtml.Length == 0,
                "a text body is stored as text, and the HTML column is left alone");
            Check(read.Message.ProcessingStatus == MailProcessing.Received,
                "and it arrives at the front of the queue, not past it");

            // Graph writes UTC. The register runs on Thai time, and a date read
            // in the server's own zone is how an evening message lands on the
            // wrong day.
            Check(read.Message.ReceivedAt.UtcDateTime == new DateTime(2026, 9, 8, 17, 4, 11, DateTimeKind.Utc),
                "the arrival time is the instant Graph sent, not the server's reading of it");

            Check(read.Participants.Count == 2, "both recipients become rows");
            Check(read.Participants[0].Kind == MailParticipant.To
                && read.Participants[1].Kind == MailParticipant.Cc, "and each keeps how they were addressed");
        }

        Console.WriteLine();
        Console.WriteLine("The shapes the documentation does not dwell on.");
        Console.WriteLine();

        // A subject longer than its column. This is what a forwarded thread
        // looks like by its fourth hop, and it fails at SaveChanges rather than
        // here if nothing cuts it.
        var longSubject = new string('x', MailText.Subject + 250);
        var oversized = GraphMessages.ReadOne(Json($$"""
        { "id": "A", "subject": "{{longSubject}}",
          "from": { "emailAddress": { "name": "{{new string('n', MailText.PersonName + 80)}}",
                                      "address": "{{new string('a', MailText.Address + 40)}}@x.co.th" } } }
        """));
        Check(oversized?.Message.Subject.Length == MailText.Subject,
            $"a subject past {MailText.Subject} is cut to the column, not to SaveChanges");
        Check(oversized?.Message.FromName.Length == MailText.PersonName, "so is a display name");
        Check(oversized?.Message.FromAddress.Length == MailText.Address, "and so is an address");

        // A message with no sender at all. Graph returns these.
        var noSender = GraphMessages.ReadOne(Json("""
        { "id": "B", "subject": "no from", "receivedDateTime": "2026-09-08T10:00:00Z" }
        """));
        Check(noSender is not null && noSender.Message.FromAddress == "" && noSender.Message.FromName == "",
            "a message with no sender reads, with the sender empty rather than a crash");

        // Falls back to when it was sent. Falling back to now would make an old
        // message look like it had just arrived and jump the queue.
        var noReceived = GraphMessages.ReadOne(Json("""
        { "id": "C", "sentDateTime": "2026-09-01T08:00:00Z" }
        """));
        Check(noReceived?.Message.ReceivedAt.UtcDateTime == new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            "a message with no arrival time falls back to when it was sent");

        var noTimes = GraphMessages.ReadOne(Json("""{ "id": "D" }"""));
        Check(noTimes is not null && noTimes.Message.ReceivedAt == default,
            "and one with neither is left empty rather than stamped with now");

        // The id is the whole of idempotency. Without it the same message would
        // be stored again on every redelivery.
        Check(GraphMessages.ReadOne(Json("""{ "subject": "no id" }""")) is null,
            "a message with no id is refused — the id is what makes a redelivery the same message");
        Check(GraphMessages.ReadOne(Json("""[1,2]""")) is null, "and so is something that is not an object");

        var html = GraphMessages.ReadOne(Json("""
        { "id": "E", "body": { "contentType": "HTML", "content": "<p>TEMU0404097</p>" } }
        """));
        Check(html?.Message.BodyHtml.Contains("<p>") == true && html?.Message.BodyText.Length == 0,
            "an HTML body is stored as HTML, not fed to the extractor as tags");

        Console.WriteLine();
        Console.WriteLine("Who a message went to.");
        Console.WriteLine();

        var recipients = GraphMessages.Participants(Json("""
        {
          "toRecipients": [
            { "emailAddress": { "address": "ops@leschaco.co.th" } },
            { "emailAddress": { "address": "OPS@leschaco.co.th" } },
            { "emailAddress": { "name": "no address" } }
          ],
          "ccRecipients": [ { "emailAddress": { "address": "ops@leschaco.co.th" } } ],
          "bccRecipients": [ { "emailAddress": { "address": "quiet@leschaco.co.th" } } ]
        }
        """));

        Check(recipients.Count(one => one.Kind == MailParticipant.To) == 1,
            "the same address twice in To is one row");
        Check(recipients.Count(one => one.Address == "ops@leschaco.co.th") == 2,
            "but To and Cc are two — that is what happened, and the screen should say so");
        Check(recipients.Any(one => one.Kind == MailParticipant.Bcc), "Bcc is kept too");
        Check(recipients.All(one => one.Address.Length > 0),
            "a recipient with no address is dropped — it can be neither written to nor matched on");

        Check(GraphMessages.Participants(Json("""{ "toRecipients": "not a list" }""")).Count == 0,
            "and a recipient list that is not a list is no recipients, not an exception");

        Console.WriteLine();
        Console.WriteLine("A page of them.");
        Console.WriteLine();

        var page = GraphMessages.ReadPage(Json("""
        {
          "@odata.nextLink": "https://graph.microsoft.com/v1.0/users/ops@leschaco.co.th/messages?$skip=50",
          "value": [
            { "id": "1", "subject": "first" },
            { "subject": "no id, and so no way to store it once" },
            { "id": "3", "subject": "third" }
          ]
        }
        """));
        // One bad item costs one message, not the other forty-nine.
        Check(page.Messages.Count == 2, "an item that cannot be read costs that item, not the page");
        Check(page.NextLink.Contains("$skip=50"), "and the link to the rest is carried out");

        var lastPage = GraphMessages.ReadPage(Json("""{ "value": [] }"""));
        Check(lastPage.Messages.Count == 0 && lastPage.NextLink.Length == 0,
            "the last page has nothing and points nowhere");
        Check(GraphMessages.ReadPage(Json("""{ }""")).Messages.Count == 0,
            "and a response with no value at all is no messages, not an exception");

        Console.WriteLine();
        Console.WriteLine("Where a paging link may point.");
        Console.WriteLine();

        // This link is requested WITH the bearer token attached, so where it
        // points is a question about where the token goes.
        Check(GraphMessages.IsGraphLink("https://graph.microsoft.com/v1.0/users/x/messages?$skip=50"),
            "Graph over HTTPS is followed");
        Check(!GraphMessages.IsGraphLink("http://graph.microsoft.com/v1.0/messages"),
            "the same host over plain HTTP is not");
        Check(!GraphMessages.IsGraphLink("https://evil.example.com/v1.0/messages"),
            "another host is not — that is where the token would have gone");
        Check(!GraphMessages.IsGraphLink("https://graph.microsoft.com.evil.example.com/messages"),
            "nor a host that merely starts with it");
        Check(!GraphMessages.IsGraphLink("https://user:pass@graph.microsoft.com/messages"),
            "nor one carrying credentials in the URL");
        Check(GraphMessages.IsGraphLink("https://GRAPH.MICROSOFT.COM/v1.0/messages"),
            "but capitalisation of a host name means nothing, so it is followed");
        Check(!GraphMessages.IsGraphLink(""), "an empty link is not followed");
        Check(!GraphMessages.IsGraphLink(null), "and neither is none at all");

        Console.WriteLine();
        Console.WriteLine("What arrived attached.");
        Console.WriteLine();

        var attachments = GraphMessages.ReadAttachments(Json("""
        {
          "value": [
            { "id": "att1", "name": "ARRIVAL NOTICE.pdf", "contentType": "application/pdf", "size": 84213 },
            { "id": "att2", "name": "logo.png", "contentType": "image/png", "size": 4096, "isInline": true },
            { "id": "att1", "name": "ARRIVAL NOTICE.pdf", "contentType": "application/pdf", "size": 84213 },
            { "id": "att3", "name": "packing list.xlsx" },
            { "name": "no id at all" }
          ]
        }
        """));

        Check(attachments.Count == 2, "two files worth keeping out of five entries");
        // Every signature in the industry carries a logo. Keeping them would put
        // thousands of copies of the same image into Blob.
        Check(attachments.All(one => one.FileName != "logo.png"),
            "an inline signature image is not a document and is dropped");
        Check(attachments.Count(one => one.GraphAttachmentId == "att1") == 1,
            "the same attachment listed twice is stored once");
        Check(attachments.Any(one => one.SizeBytes == 84213), "the size is carried");
        Check(attachments.Any(one => one.GraphAttachmentId == "att3" && one.SizeBytes == 0),
            "an entry with no size is zero, not a refusal");
        Check(attachments.All(one => one.StoredDocumentId == 0),
            "and nothing claims to hold bytes yet — fetching them is its own step");

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "All Graph message checks passed."
            : $"{failed} Graph message check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
