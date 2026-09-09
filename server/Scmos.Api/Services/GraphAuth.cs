using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// How SCMOS proves to Microsoft Graph that it is SCMOS — once, for everything.
///
/// <para>
/// <b>No client secret.</b> The Outlook plan asked for client credentials and a
/// token cache; neither is needed, and finding that out was the useful part of
/// building this. The API already holds a system-assigned managed identity and
/// already calls Graph with it — that is how a colleague gets invited from the
/// Administration screen. Granting <c>Mail.Read</c> to that same identity means
/// there is no secret to create, store, rotate, or hand to anybody. A secret
/// that does not exist cannot leak, and it cannot expire on a Sunday.
/// </para>
///
/// <para>
/// The cost of that choice, stated rather than buried: one identity then holds
/// both <c>User.ReadWrite.All</c> and <c>Mail.Read</c>. A separate app
/// registration would keep them apart, at the price of a secret somebody has to
/// look after. Exchange RBAC still narrows <c>Mail.Read</c> to the approved
/// mailboxes either way, so the separation buys less than the secret costs —
/// but it is a security posture, and it is the operator's to overrule.
/// </para>
///
/// <para>
/// <b>No token cache either.</b> <see cref="DefaultAzureCredential"/> caches
/// what it fetches and renews it near expiry. A second cache in front of it
/// would be a second opinion about when a token dies, and this repository's
/// standing lesson is that every rule written twice here has eventually
/// disagreed with itself.
/// </para>
///
/// <para>
/// A singleton, because the credential probes several sources when it is built
/// and that probe should happen once for the process rather than once per
/// request.
/// </para>
/// </summary>
public sealed class GraphAuth(IHttpClientFactory factory, IConfiguration config, ILogger<GraphAuth> log)
{
    /// <summary>The named <see cref="HttpClient"/>, registered in Program.cs.</summary>
    public const string ClientName = "graph";

    /// <summary>Where the approved mailboxes are listed — <c>Graph__Mailboxes</c> on App Service.</summary>
    public const string MailboxesKey = "Graph:Mailboxes";

    /// <summary>The application permission the Communication Center needs.</summary>
    public const string MailRead = "Mail.Read";

    public const string Endpoint = "https://graph.microsoft.com/v1.0";

    private static readonly string[] Scope = ["https://graph.microsoft.com/.default"];

    private readonly TokenCredential _credential = new DefaultAzureCredential();

    /// <summary>
    /// Parsed once. App Service restarts the container when an app setting
    /// changes, so this cannot go stale under a running process — and the mail
    /// worker asks the question once per message.
    /// </summary>
    private readonly IReadOnlyList<string> _approved = GraphMailboxes.Parse(config[MailboxesKey]);

    /// <summary>The mailboxes this deployment may read. Empty means none.</summary>
    public IReadOnlyList<string> Approved => _approved;

    /// <summary>Whether this deployment may read that mailbox. See <see cref="GraphMailboxes"/>.</summary>
    public bool Approves(string? address) => GraphMailboxes.IsApproved(address, _approved);

    /// <summary>
    /// A bearer token for Graph, or null with the reason logged.
    ///
    /// Null rather than an exception because every caller has something better
    /// to say to a person than a stack trace, and because "Graph is not
    /// reachable" is a state this integration is expected to sit in for as long
    /// as the Entra registration takes.
    /// </summary>
    public async Task<string?> TokenAsync(CancellationToken token)
    {
        try
        {
            var access = await _credential.GetTokenAsync(new TokenRequestContext(Scope), token);
            return access.Token;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The caller gave up. Not a fault, and not something to log as one.
            throw;
        }
        catch (Exception problem)
        {
            log.LogError(problem, "Could not get a Microsoft Graph token for the managed identity.");
            return null;
        }
    }

    /// <summary>
    /// A client already carrying the token, or null when there is no token.
    ///
    /// <para>
    /// Deliberately says nothing about whether the <i>mail</i> integration is
    /// switched on. Sign-in account creation authenticates through here too and
    /// has nothing to do with mail; a switch in this method would have stopped
    /// administrators inviting colleagues the day somebody turned the mailbox
    /// off. Whether a mailbox is read is <c>Mailbox.IsActive</c>'s answer, and
    /// whether it may be read at all is <see cref="Approves"/>'s.
    /// </para>
    /// </summary>
    public async Task<HttpClient?> ClientAsync(CancellationToken token)
    {
        var access = await TokenAsync(token);
        if (access is null) return null;

        var client = factory.CreateClient(ClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return client;
    }

    /// <summary>
    /// Whether mail can be read, and if not, which of the several reasons it is.
    /// </summary>
    /// <param name="Ok">Everything needed is in place.</param>
    /// <param name="Message">For an administrator, in Thai.</param>
    /// <param name="Mailboxes">The approved list, as configured.</param>
    /// <param name="Token">Whether Entra issued a token at all.</param>
    /// <param name="MailRead">Whether that token carries <c>Mail.Read</c>.</param>
    public record Ready(bool Ok, string Message, IReadOnlyList<string> Mailboxes, bool Token, bool MailRead);

    /// <summary>
    /// The readiness answer, without touching a mailbox.
    ///
    /// <para>
    /// The plan says this integration will get stuck at the first real Graph
    /// call, and it is right: a 403 from a mailbox read means missing consent,
    /// or missing Exchange RBAC scope, or a mailbox that is not there, and the
    /// three look identical. So the two faults that can be told apart
    /// <i>before</i> that call are told apart here — nothing configured, and no
    /// consent — and what is left when this reports ready is the one fault a
    /// mailbox read can actually diagnose.
    /// </para>
    ///
    /// <para>
    /// Configuration is checked before the network, so the commonest fault
    /// answers instantly and does not wait on Entra.
    /// </para>
    /// </summary>
    public async Task<Ready> ReadyAsync(CancellationToken token)
    {
        if (_approved.Count == 0)
            return new(false, "ยังไม่ได้ตั้ง Graph__Mailboxes — ยังไม่มีตู้จดหมายที่อนุญาต จึงยังไม่อ่านอะไรเลย",
                _approved, false, false);

        var wrong = _approved.Where(one => !GraphMailboxes.LooksLikeAddress(one)).ToList();
        if (wrong.Count > 0)
            return new(false, $"Graph__Mailboxes มีค่าที่ไม่ใช่อีเมล: {string.Join(", ", wrong)}",
                _approved, false, false);

        var access = await TokenAsync(token);
        if (access is null)
            return new(false, "ขอ token จาก Microsoft Graph ไม่ได้ — API ยังไม่มี managed identity หรือยังต่อ Entra ไม่ได้",
                _approved, false, false);

        if (!GraphToken.Grants(access, MailRead))
            return new(false, $"ได้ token แล้ว แต่ยังไม่มีสิทธิ์ {MailRead} — ต้อง grant application permission และ admin consent ให้ managed identity ของ API",
                _approved, true, false);

        return new(true, $"พร้อมอ่านเมล {_approved.Count} ตู้", _approved, true, true);
    }
}
