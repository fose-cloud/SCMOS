using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>Overrides for people whose role is not the default. Email → role.</summary>
    public Dictionary<string, string> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
public class UserAccessor(IOptions<AuthOptions> options, IHostEnvironment environment, ILogger<UserAccessor> log) : IUserAccessor
{
    private readonly AuthOptions _options = options.Value;

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

        var known = StaffDirectory.All.FirstOrDefault(o => o.Account == account);
        return known is null
            ? Build(account, "", account, "development")
            : new AppUser(known.Id, "", known.Name, known.Role, known.Id, "development");
    }

    private AppUser Build(string userId, string email, string displayName, string source)
    {
        var matched = StaffDirectory.Match(email, displayName);
        var role = _options.Roles.TryGetValue(email, out var configured) && configured.Length > 0
            ? configured
            : matched?.Role ?? StaffDirectory.DefaultRole;

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
