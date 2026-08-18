using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Scmos.Api.Auth;

public class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>
    /// <c>Proxy</c> — the web app is signed in with App Service Web App Login and
    /// forwards the platform's headers; this API trusts them only when
    /// <see cref="ProxyKey"/> comes with them. This is the deployed shape.
    ///
    /// <c>Platform</c> — this API is itself behind Web App Login, so the headers
    /// arrive from App Service and nothing else can set them.
    ///
    /// <c>Development</c> — no provider at all. The demo gate decides, exactly as
    /// `npm run dev` did before the move. Never valid outside Development.
    /// </summary>
    public string Mode { get; set; } = "Proxy";

    /// <summary>Shared secret between the web app and this API. Comes from Key Vault.</summary>
    public string ProxyKey { get; set; } = "";

    /// <summary>
    /// Overrides for people whose role is not the default. Email → role.
    ///
    /// Works from appsettings.json and Key Vault, where a key may be anything.
    /// It does <b>not</b> work as an App Service application setting: those names
    /// accept only letters, digits, dots and underscores, and every email address
    /// contains an <c>@</c>. Use <see cref="RoleMap"/> there.
    /// </summary>
    public Dictionary<string, string> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The same assignments in one value, for hosts that restrict setting names.
    ///
    ///   <c>Auth__RoleMap = a@x.com=Administrator; b@x.com=Operation Supervisor</c>
    ///
    /// This exists because the per-email form was written, documented, and then
    /// refused by App Service on the first real deployment — the name it needs
    /// cannot contain the one character an email always has.
    /// </summary>
    public string RoleMap { get; set; } = "";

    /// <summary>
    /// Every assignment, from both forms. <see cref="RoleMap"/> is applied second
    /// so a host-level setting can correct a value baked into a config file.
    /// </summary>
    public IReadOnlyDictionary<string, string> AllRoles()
    {
        var all = new Dictionary<string, string>(Roles, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in RoleMap.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = entry.IndexOf('=');
            if (split <= 0) continue;
            var email = entry[..split].Trim();
            var role = entry[(split + 1)..].Trim();
            if (email.Length > 0 && role.Length > 0) all[email] = role;
        }

        return all;
    }
}

public interface IUserAccessor
{
    AppUser? Current(HttpContext context);
}

/// <summary>
/// Who is asking.
///
/// App Service Web App Login puts the verified principal in
/// <c>X-MS-CLIENT-PRINCIPAL</c>. Those headers are only trustworthy when
/// something upstream is guaranteed to strip whatever the client sent — the
/// platform does that for its own app, and the web app's proxy does it for this
/// one. Outside those two cases the headers are ignored completely, because
/// anyone could set them by hand.
/// </summary>
public class UserAccessor(
    IOptions<AuthOptions> options,
    IHostEnvironment environment,
    ILogger<UserAccessor> log,
    Data.ScmosDbContext db) : IUserAccessor
{
    private readonly AuthOptions _options = options.Value;

    /// <summary>
    /// The directory, read once per request.
    ///
    /// It is a table now rather than a hardcoded array, so this cannot stay
    /// static. A failure to read it must not become a failure to sign in as
    /// anybody at all, so an unreachable directory degrades to "nobody is
    /// recognised" — which the screen already explains — rather than throwing.
    /// </summary>
    private IReadOnlyList<Data.StaffMember> Directory()
    {
        try
        {
            return db.Staff.AsNoTracking().Where(person => person.Active).ToList();
        }
        catch (Exception problem)
        {
            log.LogError(problem, "Could not read the staff directory; nobody will be recognised.");
            return [];
        }
    }

    private const string PrincipalHeader = "X-MS-CLIENT-PRINCIPAL";
    private const string NameHeader = "X-MS-CLIENT-PRINCIPAL-NAME";
    private const string IdHeader = "X-MS-CLIENT-PRINCIPAL-ID";
    private const string ProxyKeyHeader = "X-Scmos-Proxy-Key";
    private const string DevUserHeader = "X-Scmos-Dev-User";

    public AppUser? Current(HttpContext context)
    {
        return _options.Mode switch
        {
            "Platform" => FromHeaders(context),
            "Development" => environment.IsDevelopment() ? DevelopmentUser(context) : null,
            _ => ProxyIsTrusted(context) ? FromHeaders(context) : null,
        };
    }

    /// <summary>
    /// Fixed-time comparison — a shared secret checked with <c>==</c> leaks its
    /// length and prefix to anyone willing to time the responses.
    /// </summary>
    private bool ProxyIsTrusted(HttpContext context)
    {
        if (_options.ProxyKey.Length == 0)
        {
            log.LogError("Auth:Mode is Proxy but Auth:ProxyKey is not set — every request will be refused.");
            return false;
        }

        var presented = context.Request.Headers[ProxyKeyHeader].ToString();
        if (presented.Length == 0) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(_options.ProxyKey));
    }

    private AppUser? FromHeaders(HttpContext context)
    {
        var email = context.Request.Headers[NameHeader].ToString().Trim();
        var userId = context.Request.Headers[IdHeader].ToString().Trim();
        var displayName = "";

        var encoded = context.Request.Headers[PrincipalHeader].ToString();
        if (encoded.Length > 0)
        {
            var principal = Decode(encoded);
            if (principal is not null)
            {
                email = Claim(principal, "preferred_username", "emails",
                    "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress") ?? email;
                displayName = Claim(principal, "name",
                    "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name") ?? "";
                userId = Claim(principal, "oid",
                    "http://schemas.microsoft.com/identity/claims/objectidentifier") ?? userId;
            }
        }

        if (email.Length == 0 && userId.Length == 0) return null;
        if (displayName.Length == 0) displayName = NameFromEmail(email);

        return Build(userId, email, displayName, "webapp");
    }

    /// <summary>
    /// The local demo gate, unchanged in spirit: any of the eight accounts, no
    /// password. It only ever runs in Development.
    /// </summary>
    private AppUser? DevelopmentUser(HttpContext context)
    {
        var account = context.Request.Headers[DevUserHeader].ToString().Trim().ToLowerInvariant();
        if (account.Length == 0) return null;

        var known = Directory().FirstOrDefault(person =>
            string.Equals(person.Account, account, StringComparison.OrdinalIgnoreCase));
        return known is null
            ? Build(account, "", account, "development")
            : new AppUser(known.Id, "", known.Name, known.Role, known.Id, "development");
    }

    /// <summary>
    /// The one place a signed-in identity becomes a role and an owner id.
    ///
    /// Order: an explicit <c>Auth:Roles</c> entry, then the staff directory,
    /// then <see cref="Rules.Roles.Viewer"/> — not the default operator role.
    /// Somebody the directory has never heard of is a person nobody has added
    /// yet, and read-only is the honest answer to that. Giving them a writer
    /// role instead produces an account that looks like an operator, owns no
    /// jobs, and can still report fleet capacity — which is worse than an
    /// obvious lack of access somebody fixes in a minute.
    /// </summary>
    private AppUser Build(string userId, string email, string displayName, string source)
    {
        var matched = Services.StaffService.MatchIn(Directory(), email, displayName);

        // The app setting wins over the table on purpose. It is the way back in
        // when the directory says nobody is an administrator — a lockout the
        // Administration screen cannot fix from inside.
        var role = _options.AllRoles().TryGetValue(email, out var configured) && configured.Length > 0
            ? configured
            : matched?.Role ?? Rules.Roles.Viewer;

        return new AppUser(
            userId.Length > 0 ? userId : email,
            email,
            displayName.Length > 0 ? displayName : NameFromEmail(email),
            role,
            matched?.Id ?? "",
            source);
    }

    private static string NameFromEmail(string email)
    {
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var word = local.Split('.', '_', '-').FirstOrDefault(part => part.Length > 0) ?? local;
        return word.Length == 0 ? "" : char.ToUpperInvariant(word[0]) + word[1..];
    }

    private static ClientPrincipal? Decode(string encoded)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return JsonSerializer.Deserialize<ClientPrincipal>(json);
        }
        catch (Exception error) when (error is FormatException or JsonException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static string? Claim(ClientPrincipal principal, params string[] types)
    {
        foreach (var type in types)
        {
            var value = principal.Claims?.FirstOrDefault(c =>
                string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return null;
    }

    private class ClientPrincipal
    {
        [JsonPropertyName("auth_typ")] public string? AuthType { get; set; }
        [JsonPropertyName("claims")] public List<ClientPrincipalClaim>? Claims { get; set; }
    }

    private class ClientPrincipalClaim
    {
        [JsonPropertyName("typ")] public string? Type { get; set; }
        [JsonPropertyName("val")] public string? Value { get; set; }
    }
}
