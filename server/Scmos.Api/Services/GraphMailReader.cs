using System.Globalization;
using System.Text.Json;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Fetching mail out of an approved mailbox.
///
/// <para>
/// The calls only. What comes back is turned into rows by
/// <see cref="GraphMessages"/>, which is pure and checked on its own — this
/// half is the part that cannot be checked without a mailbox, so it is kept as
/// thin as it can be: build a URL, make the call, hand the body to the parser,
/// and turn whatever went wrong into a sentence.
/// </para>
///
/// <para>
/// <b>Every method asks the approved list first.</b> Before a token, before a
/// URL. A mailbox this deployment was never approved for produces no Graph call
/// at all, whatever Exchange would have said about it — the RBAC scoping is the
/// real control, and this is the one that still holds if the scoping is wrong.
/// </para>
///
/// <para>
/// Nothing here saves anything. Persistence and deduplication are step 9, and
/// keeping the fetch separate is what lets the same message be fetched twice
/// while being stored once.
/// </para>
/// </summary>
public sealed class GraphMailReader(GraphAuth graph, ILogger<GraphMailReader> log)
{
    /// <summary>
    /// Graph pages at 1,000 and defaults to 10. Fifty is a page a worker can
    /// finish and record before its lease matters, and small enough that a
    /// retry after a failure is cheap.
    /// </summary>
    public const int DefaultPageSize = 50;

    /// <summary>Long enough for a slow page, short enough to fail rather than hang a worker.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Longer, for bytes. A page of message headers that takes a minute has
    /// gone wrong; a 30 MB attachment on a bad line has not.
    /// </summary>
    private static readonly TimeSpan Download = TimeSpan.FromMinutes(5);

    /// <summary>What came back, and what to say if it did not.</summary>
    /// <param name="Finding">The diagnosis — <see cref="GraphDiagnosis"/>.</param>
    /// <param name="Value">Null whenever <c>Finding.Ok</c> is false.</param>
    public sealed record Fetched<T>(GraphDiagnosis.Finding Finding, T? Value)
    {
        public bool Ok => Finding.Ok && Value is not null;
    }

    /// <summary>
    /// The newest messages in a mailbox, oldest-first from a point in time.
    ///
    /// <para>
    /// Ordered by arrival ascending rather than descending, because this is how
    /// a mailbox is caught up: read forward from where we stopped. Descending
    /// would mean a catch-up that starts at the newest message and can never
    /// safely record how far it got.
    /// </para>
    /// </summary>
    /// <param name="since">Read from this moment on. Null reads from the beginning.</param>
    public async Task<Fetched<GraphMessages.Page>> PageAsync(string mailbox, DateTimeOffset? since,
        CancellationToken token, int size = DefaultPageSize)
    {
        var address = GraphMailboxes.Normalise(mailbox);
        if (!graph.Approves(address)) return Refused<GraphMessages.Page>(GraphDiagnosis.NotApproved);

        var url = $"{GraphAuth.Endpoint}/users/{Uri.EscapeDataString(address)}/messages"
            + $"?$top={Math.Clamp(size, 1, 999)}"
            + $"&$select={Uri.EscapeDataString(GraphMessages.Fields)}"
            + "&$orderby=" + Uri.EscapeDataString("receivedDateTime asc");

        if (since is { } from)
        {
            // Graph wants ISO 8601 in UTC. Formatted explicitly rather than
            // left to the current culture, which is how a Thai-locale server
            // sends a Buddhist year to Microsoft and gets nothing back.
            var moment = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            url += "&$filter=" + Uri.EscapeDataString($"receivedDateTime ge {moment}");
        }

        return await PageAtAsync(url, address, token);
    }

    /// <summary>
    /// The next page, from the link the last one carried.
    ///
    /// The link is checked against <see cref="GraphMessages.IsGraphLink"/>
    /// before it is followed, because following it attaches the bearer token to
    /// whatever host it names.
    /// </summary>
    public async Task<Fetched<GraphMessages.Page>> NextAsync(string nextLink, CancellationToken token)
    {
        if (!GraphMessages.IsGraphLink(nextLink))
        {
            log.LogWarning("Refusing to follow a paging link that is not Microsoft Graph");
            return Refused<GraphMessages.Page>(GraphDiagnosis.BadLink);
        }
        return await PageAtAsync(nextLink, "", token);
    }

    /// <summary>
    /// One message by its Graph id — what a change notification names.
    ///
    /// Ok with a null value is a real answer: the notification arrived, and by
    /// the time it was acted on the message had been deleted or moved. That is
    /// ordinary and must not be recorded as a failure.
    /// </summary>
    public async Task<Fetched<GraphMessages.Read?>> MessageAsync(string mailbox, string messageId,
        CancellationToken token)
    {
        var address = GraphMailboxes.Normalise(mailbox);
        if (!graph.Approves(address)) return Refused<GraphMessages.Read?>(GraphDiagnosis.NotApproved);
        if (string.IsNullOrWhiteSpace(messageId))
            return Refused<GraphMessages.Read?>(GraphDiagnosis.ForStatus(400, true));

        var url = $"{GraphAuth.Endpoint}/users/{Uri.EscapeDataString(address)}"
            + $"/messages/{Uri.EscapeDataString(messageId)}"
            + $"?$select={Uri.EscapeDataString(GraphMessages.Fields)}";

        var (finding, body) = await GetAsync(url, address, token);
        if (body is null) return new(finding, null);
        using (body)
            // A message that has gone is not a fault; 404 was already turned
            // into its own finding, so a null here is a body we could not read.
            return new(finding, GraphMessages.ReadOne(body.RootElement));
    }

    /// <summary>
    /// What a message has attached, as metadata. The bytes are step 13's.
    /// </summary>
    public async Task<Fetched<IReadOnlyList<EmailAttachment>>> AttachmentsAsync(string mailbox,
        string messageId, CancellationToken token)
    {
        var address = GraphMailboxes.Normalise(mailbox);
        if (!graph.Approves(address))
            return Refused<IReadOnlyList<EmailAttachment>>(GraphDiagnosis.NotApproved);

        var url = $"{GraphAuth.Endpoint}/users/{Uri.EscapeDataString(address)}"
            + $"/messages/{Uri.EscapeDataString(messageId)}/attachments"
            + "?$select=" + Uri.EscapeDataString("id,name,contentType,size,isInline");

        var (finding, body) = await GetAsync(url, address, token);
        if (body is null) return new(finding, null);
        using (body) return new(finding, GraphMessages.ReadAttachments(body.RootElement));
    }

    /// <summary>
    /// One attachment's bytes, written to a file on disk.
    ///
    /// <para>
    /// To disk rather than to memory. A worker holding thirty megabytes per
    /// attachment in a byte array, with several in flight, is a container that
    /// dies of an allocation on a Tuesday afternoon for no reason anybody can
    /// reconstruct. The file is opened <c>DeleteOnClose</c>, so it goes when the
    /// stream is disposed — including when the upload throws, and including
    /// when the process is killed part way through, because Windows and Linux
    /// both drop the handle.
    /// </para>
    ///
    /// <para>
    /// <c>/$value</c> rather than reading <c>contentBytes</c> out of the
    /// attachment JSON: the JSON route base64-encodes the file into a response
    /// body, which is a third larger and has to be decoded in memory, and Graph
    /// will not serve it at all past about three megabytes.
    /// </para>
    /// </summary>
    /// <returns>
    /// A stream positioned at the start, which the caller disposes. Null
    /// whenever the finding is not Ok.
    /// </returns>
    public async Task<Fetched<Stream>> AttachmentBytesAsync(string mailbox, string messageId,
        string attachmentId, CancellationToken token)
    {
        var address = GraphMailboxes.Normalise(mailbox);
        if (!graph.Approves(address)) return Refused<Stream>(GraphDiagnosis.NotApproved);

        var url = $"{GraphAuth.Endpoint}/users/{Uri.EscapeDataString(address)}"
            + $"/messages/{Uri.EscapeDataString(messageId)}"
            + $"/attachments/{Uri.EscapeDataString(attachmentId)}/$value";

        var access = await graph.TokenAsync(token);
        if (access is null) return Refused<Stream>(GraphDiagnosis.NoToken);
        var consented = GraphToken.Grants(access, GraphAuth.MailRead);

        var client = await graph.ClientAsync(token);
        if (client is null) return Refused<Stream>(GraphDiagnosis.NoToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(token);
        patience.CancelAfter(Download);

        HttpResponseMessage response;
        try
        {
            // Headers first. Without this the whole file is buffered by
            // HttpClient before a single line of this method runs again, which
            // is the allocation the temporary file exists to avoid.
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, patience.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            log.LogWarning("Downloading an attachment from {Mailbox} timed out after {Seconds}s",
                address, Download.TotalSeconds);
            return Refused<Stream>(GraphDiagnosis.ForStatus(504, true));
        }
        catch (HttpRequestException problem)
        {
            log.LogError(problem, "Could not reach Microsoft Graph to download from {Mailbox}", address);
            return Refused<Stream>(GraphDiagnosis.ForStatus(503, true));
        }

        using (response)
        {
            var finding = GraphDiagnosis.ForStatus((int)response.StatusCode, consented);
            if (!finding.Ok)
            {
                log.LogWarning("Downloading an attachment from {Mailbox}: {Code} ({Status})",
                    address, finding.Code, (int)response.StatusCode);
                return Refused<Stream>(finding);
            }

            var spool = new FileStream(Path.Combine(Path.GetTempPath(), $"scmos-mail-{Guid.NewGuid():N}"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            try
            {
                await response.Content.CopyToAsync(spool, patience.Token);
                spool.Position = 0;
                return new(finding, spool);
            }
            catch (Exception problem)
            {
                // Dispose here, not by the caller: the caller is handed null and
                // has nothing to dispose, and the file would otherwise sit in
                // the temporary directory until the container restarted.
                await spool.DisposeAsync();
                if (problem is OperationCanceledException && !token.IsCancellationRequested)
                {
                    log.LogWarning("An attachment from {Mailbox} stopped part way down", address);
                    return Refused<Stream>(GraphDiagnosis.ForStatus(504, true));
                }
                if (problem is OperationCanceledException) throw;
                log.LogError(problem, "Could not write an attachment from {Mailbox} to disk", address);
                return Refused<Stream>(GraphDiagnosis.ForStatus(502, true));
            }
        }
    }

    private async Task<Fetched<GraphMessages.Page>> PageAtAsync(string url, string mailbox,
        CancellationToken token)
    {
        var (finding, body) = await GetAsync(url, mailbox, token);
        if (body is null) return new(finding, null);

        using (body)
        {
            var page = GraphMessages.ReadPage(body.RootElement);
            // A page that arrived with items the parser could not read is a
            // page that quietly lost mail. Counted, so it shows up as a number
            // somebody can look at rather than as nothing at all.
            var arrived = body.RootElement.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : 0;
            if (arrived != page.Messages.Count)
                log.LogWarning("{Skipped} of {Arrived} messages could not be read from {Mailbox}",
                    arrived - page.Messages.Count, arrived, mailbox);

            return new(finding, page);
        }
    }

    /// <summary>
    /// One GET, with the token, the timeout and the diagnosis all in one place.
    ///
    /// The document is returned open — the caller disposes it — because parsing
    /// it here would mean this method knowing what every caller wanted out of it.
    /// </summary>
    private async Task<(GraphDiagnosis.Finding Finding, JsonDocument? Body)> GetAsync(string url,
        string mailbox, CancellationToken token)
    {
        var access = await graph.TokenAsync(token);
        if (access is null) return (GraphDiagnosis.NoToken, null);
        var consented = GraphToken.Grants(access, GraphAuth.MailRead);

        var client = await graph.ClientAsync(token);
        if (client is null) return (GraphDiagnosis.NoToken, null);

        // Plain text, in one call. Graph will convert the body for us, which is
        // what the extractor needs to read a container number without tags in
        // the middle of it. The HTML a person is shown is fetched when somebody
        // opens the message, so nothing is fetched twice for the common case of
        // a message nobody reads.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Prefer", "outlook.body-content-type=\"text\"");

        using var patience = CancellationTokenSource.CreateLinkedTokenSource(token);
        patience.CancelAfter(Patience);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, patience.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            log.LogWarning("Reading {Mailbox} timed out after {Seconds}s", mailbox, Patience.TotalSeconds);
            return (GraphDiagnosis.ForStatus(504, true), null);
        }
        catch (HttpRequestException problem)
        {
            log.LogError(problem, "Could not reach Microsoft Graph to read {Mailbox}", mailbox);
            return (GraphDiagnosis.ForStatus(503, true), null);
        }

        using (response)
        {
            var finding = GraphDiagnosis.ForStatus((int)response.StatusCode, consented);
            if (!finding.Ok)
            {
                // The status and the mailbox, never the body. A Graph error on
                // a mail route can quote the address of whoever the message was
                // about, and a log is a wider audience than the mailbox is.
                log.LogWarning("Reading {Mailbox}: {Code} ({Status})",
                    mailbox, finding.Code, (int)response.StatusCode);
                return (finding, null);
            }

            try
            {
                return (finding, await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(patience.Token), cancellationToken: patience.Token));
            }
            catch (JsonException problem)
            {
                log.LogError(problem, "Microsoft Graph returned something that is not JSON for {Mailbox}", mailbox);
                return (GraphDiagnosis.ForStatus(502, true), null);
            }
        }
    }

    private static Fetched<T> Refused<T>(GraphDiagnosis.Finding finding) => new(finding, default);
}
