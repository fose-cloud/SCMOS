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
/// look after. Note that an Entra <c>Mail.Read</c> is NOT narrowed by Exchange
/// RBAC for Applications — Microsoft treats the two as a union — so in its own
/// tenant this identity reads every mailbox unless an Application Access Policy
/// restricts it. The company's mail is read the other way (below), where Exchange
/// RBAC is the only grant.
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
///
/// <para>
/// <b>Across tenants (4 Oct 2026).</b> The company's mail is in another tenant than SCMOS, which the managed identity
/// can never be given. With <c>Graph__MailTenantId</c> and <c>Graph__MailClientId</c> set, the token comes instead from
/// a multi-tenant app that trusts the managed identity through a federated credential — still no secret — and access is
/// Exchange RBAC's alone (<see cref="GraphMailIdentity"/>).
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

    /// <summary>
    /// Which identity reads the mail, from <c>Graph__MailTenantId</c>, <c>Graph__MailClientId</c> and
    /// <c>Graph__MailIdentityClientId</c>.
    /// </summary>
    private readonly GraphMailIdentity.Setting _identity = GraphMailIdentity.Read(config[GraphMailIdentity.TenantKey],
        config[GraphMailIdentity.ClientKey], config[GraphMailIdentity.IdentityKey]);

    private TokenCredential? _built;

    /// <summary>
    /// SCMOS's own tenant, always (5 Oct 2026): inviting a colleague and reading the directory are the managed identity's
    /// work and never follow the mail settings. On 4 Oct the mail credential became switchable and the invitations went
    /// through it too — set the cross-tenant ids and "send invitation" asked the company's tenant, and failed.
    /// </summary>
    private readonly TokenCredential _directory = new DefaultAzureCredential();

    /// <summary>Built on first use: a misconfigured pair builds nothing and asks Entra for nothing.</summary>
    private TokenCredential? Credential => _built ??= _identity.Mode switch
    {
        GraphMailIdentity.Mode.ManagedIdentity => new DefaultAzureCredential(),
        GraphMailIdentity.Mode.CrossTenant => CrossTenant(_identity),
        _ => null,
    };

    /// <summary>
    /// The app's token for the company's tenant, vouched for by the user-assigned managed identity its federated
    /// credential trusts — no secret anywhere. Only a user-assigned identity can be that credential (5 Oct 2026).
    /// </summary>
    private static ClientAssertionCredential CrossTenant(GraphMailIdentity.Setting identity)
    {
        var managed = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(identity.Identity));
        return new ClientAssertionCredential(identity.Tenant, identity.Client, async token =>
            (await managed.GetTokenAsync(new TokenRequestContext([GraphMailIdentity.TokenExchange]), token)).Token);
    }

    /// <summary>Which identity reads the mail.</summary>
    public GraphMailIdentity.Mode Mode => _identity.Mode;

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
        if (Credential is not { } credential)
        {
            log.LogWarning("Microsoft Graph mail identity misconfigured: {Problem}", _identity.Problem);
            return null;
        }
        try
        {
            var access = await credential.GetTokenAsync(new TokenRequestContext(Scope), token);
            return access.Token;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The caller gave up. Not a fault, and not something to log as one.
            throw;
        }
        catch (Exception problem)
        {
            log.LogError(problem, "Could not get a Microsoft Graph token ({Mode}).", _identity.Mode);
            return null;
        }
    }

    /// <summary>
    /// A client for reading mail, carrying the mail identity's token — only one <see cref="AccessAsync"/> allows, so no
    /// reader can go around it. Null when there is none.
    ///
    /// <para>
    /// Mail only. Sign-in accounts have their own, <see cref="DirectoryClientAsync"/>: had they gone on sharing this,
    /// the mail settings would decide whether an administrator can invite a colleague. Whether a mailbox is read is
    /// <c>Mailbox.IsActive</c>'s answer, and whether it may be read at all is <see cref="Approves"/>'s.
    /// </para>
    /// </summary>
    public async Task<HttpClient?> ClientAsync(CancellationToken token)
    {
        if ((await AccessAsync(token)).Token is not { } access) return null;

        var client = factory.CreateClient(ClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return client;
    }

    /// <summary>
    /// A client for SCMOS's own directory — inviting and finding colleagues — carrying the managed identity's token in
    /// SCMOS's tenant, whatever the mail settings say. Null when Entra would not issue one.
    /// </summary>
    public async Task<HttpClient?> DirectoryClientAsync(CancellationToken token)
    {
        string access;
        try
        {
            access = (await _directory.GetTokenAsync(new TokenRequestContext(Scope), token)).Token;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception problem)
        {
            log.LogError(problem, "Could not get a Microsoft Graph token for the managed identity (directory).");
            return null;
        }
        var client = factory.CreateClient(ClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return client;
    }

    /// <summary>The mail identity's token and what it means.</summary>
    /// <param name="Token">The bearer token, or null when there is none or it may not be used.</param>
    /// <param name="Consented">
    /// Whether a 403 means Exchange scoping rather than missing consent: the token's Mail.Read claim for the managed
    /// identity; always true across tenants, where Exchange RBAC is the only grant there is.
    /// </param>
    /// <param name="Refusal">Why the token may not be used: none, a misconfiguration, or a grant wider than allowed.</param>
    public record Access(string? Token, bool Consented, GraphDiagnosis.Finding? Refusal);

    /// <summary>
    /// The token and what it means, for every caller that reads mail — the one place the identity's rules live, rather
    /// than one copy per reader.
    /// </summary>
    public async Task<Access> AccessAsync(CancellationToken token)
    {
        if (_identity.Mode == GraphMailIdentity.Mode.Misconfigured)
            return new(null, false, GraphDiagnosis.Misconfigured(_identity.Problem));
        var access = await TokenAsync(token);
        var crossTenant = _identity.Mode == GraphMailIdentity.Mode.CrossTenant;
        if (access is null) return new(null, false, crossTenant ? GraphDiagnosis.NoCrossTenantToken : GraphDiagnosis.NoToken);
        var granted = GraphToken.Grants(access, MailRead);
        if (GraphMailIdentity.TooBroad(_identity.Mode, granted))
        {
            log.LogWarning("Microsoft Graph: the cross-tenant app holds tenant-wide Mail.Read; mail is not read until it is withdrawn");
            return new(null, false, GraphDiagnosis.TooBroad);
        }
        return new(access, crossTenant || granted, null);
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

        if (_identity.Mode != GraphMailIdentity.Mode.ManagedIdentity)
        {
            // Across tenants a token without Mail.Read is the expected shape: Exchange RBAC grants per mailbox, and
            // the mailbox test is what proves it. A token WITH Mail.Read is the one that may not be used.
            var cross = await AccessAsync(token);
            return cross.Refusal is { } refusal
                ? new(false, refusal.Message, _approved, cross.Token is not null, false)
                : new(true, $"พร้อมอ่านเมล {_approved.Count} ตู้ข้าม tenant — สิทธิ์ผ่าน Exchange RBAC: ทดสอบแต่ละตู้ก่อนเปิดใช้",
                    _approved, true, true);
        }
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
