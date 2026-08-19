using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace Scmos.Api.Services;

/// <summary>
/// Creating the thing a person actually signs in with.
///
/// The Administration screen used to add a row to the staff directory and call
/// that "creating an account". It is not. The row says what somebody may do
/// once they are through the door; it does not build them a door. An
/// administrator added a colleague, told them to sign in, and the sign-in
/// failed — because Microsoft had never heard of them. Nothing on the screen
/// distinguished the two, so the only way to find out was for the new person to
/// try and fail.
///
/// There are two doors, and which one is right depends on what the person
/// already has:
///
/// <b>Invitation</b> — they already have an email that can authenticate them:
/// Gmail, Outlook.com, a company address. Microsoft works out how to verify
/// them (their Google account, their existing Microsoft account, or a code
/// emailed to them) and SCMOS never handles a password. This is the normal
/// case, and it is the one that makes "sign in with whatever you already use"
/// true without SCMOS growing a password database.
///
/// <b>Tenant account</b> — they have no usable email. A real account is created
/// in the directory with a temporary password, which Entra forces them to
/// replace on first sign-in. SCMOS shows that password to the administrator
/// once and never stores it.
/// </summary>
public class SignInAccountService(
    IHttpClientFactory factory,
    IConfiguration configuration,
    ILogger<SignInAccountService> log)
{
    private const string Graph = "https://graph.microsoft.com/v1.0";
    private static readonly string[] Scope = ["https://graph.microsoft.com/.default"];

    /// <summary>
    /// Why an account could not be created, in words an administrator can act
    /// on. A raw Graph error tells them nothing they can do anything about.
    /// </summary>
    public record Outcome(bool Ok, string Message, string SignInName = "", string TempPassword = "");

    /// <summary>
    /// Built once. <see cref="DefaultAzureCredential"/> probes several sources
    /// on construction, and this service is scoped — a new one per request
    /// would put that probe in front of every screen that lists staff.
    /// </summary>
    private static readonly TokenCredential Shared = new DefaultAzureCredential();

    private async Task<HttpClient?> GraphClientAsync(CancellationToken token)
    {
        try
        {
            var access = await Shared.GetTokenAsync(new TokenRequestContext(Scope), token);
            var client = factory.CreateClient("graph");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access.Token);
            return client;
        }
        catch (Exception problem)
        {
            log.LogError(problem, "Could not get a Microsoft Graph token for the managed identity.");
            return null;
        }
    }

    /// <summary>
    /// Whether this API can create sign-ins at all, so the screen can say so
    /// before an administrator fills in a form that cannot succeed.
    /// </summary>
    public async Task<(bool Ready, string Why)> ReadyAsync(CancellationToken token)
    {
        var client = await GraphClientAsync(token);
        if (client is null)
            return (false, "API ยังไม่มี managed identity หรือขอ token จาก Microsoft Graph ไม่ได้");

        // Reading one user is the cheapest thing that fails in exactly the same
        // way creating one would: no directory permission, no answer.
        var response = await client.GetAsync($"{Graph}/users?$top=1&$select=id", token);
        if (response.IsSuccessStatusCode) return (true, "");

        return response.StatusCode == System.Net.HttpStatusCode.Forbidden
            ? (false, "managed identity ของ API ยังไม่มีสิทธิ์ User.ReadWrite.All และ User.Invite.All บน Microsoft Graph")
            : (false, $"Microsoft Graph ตอบ {(int)response.StatusCode}");
    }

    /// <summary>
    /// Invites an address — any address — and returns the name it will sign in
    /// under.
    ///
    /// That returned name matters more than it looks. A guest does not sign in
    /// as <c>someone@gmail.com</c>; the directory knows them as
    /// <c>someone_gmail.com#EXT#@tenant.onmicrosoft.com</c>, and the staff row
    /// has to hold that exact string or the person signs in successfully and
    /// SCMOS still does not recognise them. That mismatch is precisely how the
    /// first invited account ended up locked out of its own jobs.
    /// </summary>
    public async Task<Outcome> InviteAsync(string email, string displayName, CancellationToken token)
    {
        var client = await GraphClientAsync(token);
        if (client is null) return new Outcome(false, "ต่อ Microsoft Graph ไม่ได้");

        var redirect = configuration["SignIn:InviteRedirectUrl"];
        if (string.IsNullOrWhiteSpace(redirect))
            return new Outcome(false, "ยังไม่ได้ตั้งค่า SignIn:InviteRedirectUrl (ที่อยู่เว็บ SCMOS)");

        var body = JsonSerializer.Serialize(new
        {
            invitedUserEmailAddress = email,
            invitedUserDisplayName = displayName,
            inviteRedirectUrl = redirect,
            sendInvitationMessage = true,
        });

        var response = await client.PostAsync($"{Graph}/invitations",
            new StringContent(body, Encoding.UTF8, "application/json"), token);
        var text = await response.Content.ReadAsStringAsync(token);

        if (!response.IsSuccessStatusCode)
            return new Outcome(false, Explain(response.StatusCode, text, "เชิญ"));

        using var parsed = JsonDocument.Parse(text);
        var upn = parsed.RootElement.TryGetProperty("invitedUser", out var invited)
                  && invited.TryGetProperty("userPrincipalName", out var name)
            ? name.GetString() ?? ""
            : "";

        // Graph does not always return the UPN on the invitation itself; ask
        // for it rather than storing a blank and discovering it at sign-in.
        if (upn.Length == 0) upn = await FindByMailAsync(client, email, token);

        return upn.Length == 0
            ? new Outcome(false, "ส่งคำเชิญแล้ว แต่อ่านชื่อผู้ใช้ที่สร้างขึ้นไม่ได้ — ยังจับคู่กับทะเบียนไม่ได้")
            : new Outcome(true, $"ส่งคำเชิญไปที่ {email} แล้ว", upn);
    }

    /// <summary>
    /// Creates a directory account with a temporary password the person must
    /// replace the first time they sign in.
    ///
    /// The password is generated here, handed back once, and never written
    /// anywhere — not to the staff row, not to the audit trail. An
    /// administrator who loses it issues a new one; that is the correct cost.
    /// </summary>
    public async Task<Outcome> CreateTenantAccountAsync(string displayName, string account,
        CancellationToken token)
    {
        var client = await GraphClientAsync(token);
        if (client is null) return new Outcome(false, "ต่อ Microsoft Graph ไม่ได้");

        var domain = configuration["SignIn:TenantDomain"];
        if (string.IsNullOrWhiteSpace(domain))
            return new Outcome(false, "ยังไม่ได้ตั้งค่า SignIn:TenantDomain");

        var nickname = new string(account.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();
        if (nickname.Length == 0) return new Outcome(false, "ชื่อบัญชีต้องมีตัวอักษรหรือตัวเลขอย่างน้อยหนึ่งตัว");

        var upn = $"{nickname}@{domain}";
        var temporary = TemporaryPassword();

        var body = JsonSerializer.Serialize(new
        {
            accountEnabled = true,
            displayName,
            mailNickname = nickname,
            userPrincipalName = upn,
            passwordProfile = new
            {
                password = temporary,
                forceChangePasswordNextSignIn = true,
            },
        });

        var response = await client.PostAsync($"{Graph}/users",
            new StringContent(body, Encoding.UTF8, "application/json"), token);

        if (!response.IsSuccessStatusCode)
            return new Outcome(false,
                Explain(response.StatusCode, await response.Content.ReadAsStringAsync(token), "สร้างบัญชี"));

        return new Outcome(true,
            $"สร้างบัญชี {upn} แล้ว — ต้องเปลี่ยนรหัสผ่านเมื่อเข้าใช้ครั้งแรก", upn, temporary);
    }

    /// <summary>
    /// Issues a new temporary password for a directory account.
    ///
    /// Only for accounts this organisation owns. A guest signs in with their own
    /// Microsoft account, and no organisation can reset a password it does not
    /// hold — that is the point of a guest. Saying so plainly matters more than
    /// it looks: an administrator who is told "failed" will try again, and one
    /// who is told the password belongs to the person will pick up the phone.
    /// </summary>
    public async Task<Outcome> ResetPasswordAsync(string signInName, CancellationToken token)
    {
        var name = (signInName ?? "").Trim();
        if (name.Length == 0) return new Outcome(false, "ไม่รู้ว่าจะรีเซ็ตบัญชีไหน");

        if (name.Contains("#EXT#", StringComparison.OrdinalIgnoreCase))
            return new Outcome(false,
                "บัญชีนี้เป็นผู้ใช้ภายนอกที่ได้รับเชิญ รหัสผ่านเป็นของบัญชี Microsoft ส่วนตัวของเขา " +
                "องค์กรตั้งรหัสให้ไม่ได้ — ให้เจ้าตัวเปลี่ยนเองที่ account.live.com");

        var client = await GraphClientAsync(token);
        if (client is null) return new Outcome(false, "ต่อ Microsoft Graph ไม่ได้");

        var temporary = TemporaryPassword();
        var body = JsonSerializer.Serialize(new
        {
            passwordProfile = new { password = temporary, forceChangePasswordNextSignIn = true },
        });

        var response = await client.PatchAsync($"{Graph}/users/{Uri.EscapeDataString(name)}",
            new StringContent(body, Encoding.UTF8, "application/json"), token);

        if (!response.IsSuccessStatusCode)
            return new Outcome(false,
                Explain(response.StatusCode, await response.Content.ReadAsStringAsync(token), "ตั้งรหัสผ่านใหม่"));

        return new Outcome(true,
            "ตั้งรหัสผ่านชั่วคราวใหม่แล้ว — ผู้ใช้ต้องเปลี่ยนรหัสเมื่อเข้าใช้ครั้งถัดไป", name, temporary);
    }

    private static async Task<string> FindByMailAsync(HttpClient client, string email, CancellationToken token)
    {
        var escaped = email.Replace("'", "''");
        var response = await client.GetAsync(
            $"{Graph}/users?$filter=mail eq '{Uri.EscapeDataString(escaped)}'&$select=userPrincipalName", token);
        if (!response.IsSuccessStatusCode) return "";

        using var parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return parsed.RootElement.TryGetProperty("value", out var list) && list.GetArrayLength() > 0
               && list[0].TryGetProperty("userPrincipalName", out var name)
            ? name.GetString() ?? ""
            : "";
    }

    private static string Explain(System.Net.HttpStatusCode status, string body, string verb) => status switch
    {
        System.Net.HttpStatusCode.Forbidden =>
            $"{verb}ไม่ได้ — managed identity ของ API ยังไม่มีสิทธิ์บน Microsoft Graph",
        System.Net.HttpStatusCode.Unauthorized =>
            $"{verb}ไม่ได้ — Microsoft Graph ปฏิเสธ token ของ API",
        System.Net.HttpStatusCode.BadRequest when body.Contains("already exists", StringComparison.OrdinalIgnoreCase) =>
            "มีบัญชีนี้ในไดเรกทอรีอยู่แล้ว",
        _ => $"{verb}ไม่สำเร็จ ({(int)status})",
    };

    /// <summary>
    /// A temporary password that satisfies Entra's complexity rule and is meant
    /// to be read aloud once. Ambiguous characters are left out on purpose:
    /// this gets dictated over a phone more often than it gets copied.
    /// </summary>
    private static string TemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%*?";

        var characters = new List<char>
        {
            Pick(upper), Pick(lower), Pick(digits), Pick(symbols),
        };
        var pool = upper + lower + digits + symbols;
        while (characters.Count < 16) characters.Add(Pick(pool));

        // Otherwise the first four positions always hold the same four classes.
        for (var i = characters.Count - 1; i > 0; i--)
        {
            var j = System.Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }

        return new string(characters.ToArray());

        static char Pick(string from) =>
            from[System.Security.Cryptography.RandomNumberGenerator.GetInt32(from.Length)];
    }
}
