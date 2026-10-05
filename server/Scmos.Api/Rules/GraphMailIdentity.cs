namespace Scmos.Api.Rules;

/// <summary>
/// Which identity reads the mail, and what it is allowed to be (4 Oct 2026). Pure, proved by <c>--check-graph</c>.
///
/// <para>
/// SCMOS runs in its own Entra tenant; the company's mail is in another (leschaco.com). The API's managed identity can
/// only ever be given mailboxes of its own tenant, so it cannot read the company's. The company's mail is read instead by
/// a multi-tenant app registration in SCMOS's tenant that trusts the managed identity through a federated credential:
/// the identity proves itself to Entra, Entra issues the app a token for the company's tenant, and no secret exists
/// anywhere. Set <c>Graph__MailTenantId</c> and <c>Graph__MailClientId</c> for that; leave both empty and the
/// managed identity reads its own tenant, as before.
/// </para>
///
/// <para>
/// <b>The identity that vouches is a user-assigned one</b> (5 Oct 2026, <c>Graph__MailIdentityClientId</c>). Microsoft
/// lets an app's federated credential trust only a user-assigned managed identity — the Entra admin center offers no
/// other — and the API's own system-assigned identity stays where it is for everything else (the directory, storage).
/// Across tenants all three settings are required.
/// </para>
///
/// <para>
/// <b>Exchange RBAC is the only grant across tenants.</b> Microsoft's rule: a Mail.Read consented in Entra and a
/// Mail.Read assigned in Exchange RBAC for Applications are a union — the Entra grant reads every mailbox whatever the
/// RBAC scope says. So the company's admin assigns <c>Application Mail.Read</c> on a management scope holding only the
/// booking mailboxes and consents nothing in Entra; a token that does carry Mail.Read means somebody consented it, the
/// app can read the whole company's mail, and SCMOS refuses to read until the grant is withdrawn.
/// </para>
/// </summary>
public static class GraphMailIdentity
{
    /// <summary>The company's tenant — <c>Graph__MailTenantId</c> on App Service.</summary>
    public const string TenantKey = "Graph:MailTenantId";

    /// <summary>The multi-tenant app's client id — <c>Graph__MailClientId</c>.</summary>
    public const string ClientKey = "Graph:MailClientId";

    /// <summary>
    /// The client id of the user-assigned managed identity, attached to the API, that the app's federated credential
    /// trusts — <c>Graph__MailIdentityClientId</c>.
    /// </summary>
    public const string IdentityKey = "Graph:MailIdentityClientId";

    /// <summary>What the managed identity asks for to vouch for the app: Entra's token-exchange audience.</summary>
    public const string TokenExchange = "api://AzureADTokenExchange/.default";

    public enum Mode { ManagedIdentity, CrossTenant, Misconfigured }

    /// <param name="Problem">Empty unless <see cref="Mode.Misconfigured"/>: what to correct, in Thai.</param>
    /// <param name="Identity">Across tenants, the user-assigned managed identity's client id; empty otherwise.</param>
    public sealed record Setting(Mode Mode, string Tenant, string Client, string Problem, string Identity = "");

    /// <summary>
    /// Tenant and app both empty: the managed identity. Both set, with the vouching identity: across tenants. Anything
    /// else is refused, not guessed.
    /// </summary>
    public static Setting Read(string? tenant, string? client, string? identity = null)
    {
        var (t, c, i) = ((tenant ?? "").Trim(), (client ?? "").Trim(), (identity ?? "").Trim());
        if (t.Length == 0 && c.Length == 0) return new(Mode.ManagedIdentity, "", "", "");
        if (t.Length == 0 || c.Length == 0)
            return new(Mode.Misconfigured, t, c, "ต้องตั้ง Graph__MailTenantId และ Graph__MailClientId คู่กัน — ตอนนี้มีค่าเดียว");
        if (!Guid.TryParse(t, out _)) return new(Mode.Misconfigured, t, c, "Graph__MailTenantId ต้องเป็น Tenant ID (GUID) ของ tenant ที่เก็บอีเมล");
        if (!Guid.TryParse(c, out _)) return new(Mode.Misconfigured, t, c, "Graph__MailClientId ต้องเป็น Application (client) ID (GUID) ของแอป SCMOS Mail Reader");
        if (!Guid.TryParse(i, out _))
            return new(Mode.Misconfigured, t, c, "ต้องตั้ง Graph__MailIdentityClientId เป็น Client ID (GUID) ของ user-assigned managed identity "
                + "ที่ผูกกับ scmos-api-3936 — federated credential ของแอปเชื่อถือได้เฉพาะ user-assigned");
        return new(Mode.CrossTenant, t, c, "", i);
    }

    /// <summary>
    /// Whether a cross-tenant token may not be used: it carries Entra's tenant-wide Mail.Read, which no RBAC scope
    /// narrows. The managed identity's own tenant keeps its existing reading.
    /// </summary>
    public static bool TooBroad(Mode mode, bool tokenGrantsMailRead) => mode == Mode.CrossTenant && tokenGrantsMailRead;
}
