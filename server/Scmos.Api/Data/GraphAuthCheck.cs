using System.Text;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// The two decisions Graph authentication makes before it touches a network,
/// with <c>--check-graph</c>.
///
/// <para>
/// Which mailboxes this deployment may read, and what Entra says it granted.
/// Both are string work, so both can be got right — and kept right — with no
/// Entra registration, no managed identity and no mailbox. That matters more
/// here than usual: everything else in the Outlook integration is blocked on an
/// Azure change, so these are the parts that can be finished now.
/// </para>
///
/// <para>
/// <b>No token appears below.</b> The JWTs are built here out of public JSON
/// with an empty signature — the shape Entra sends, carrying nothing. A check
/// that needed a real token would be a check that could not run in CI, and a
/// credential in a repository.
/// </para>
/// </summary>
public static class GraphAuthCheck
{
    /// <summary>A JWT's payload segment: base64url, unpadded, as a real one arrives.</summary>
    private static string Segment(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A structurally real token with no signature and nothing secret in it.</summary>
    private static string Jwt(string payload) => $"{Segment("{\"alg\":\"none\"}")}.{Segment(payload)}.";

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-graph")) return null;

        var failed = 0;

        void Check(bool ok, string why)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        Console.WriteLine();
        Console.WriteLine("Which mailboxes this deployment is allowed to read.");
        Console.WriteLine();

        var list = GraphMailboxes.Parse("ops@leschaco.co.th, import@leschaco.co.th");
        Check(list.Count == 2, "a comma-separated setting is two mailboxes");

        Check(GraphMailboxes.Parse("a@x.co.th;b@x.co.th").Count == 2, "so is a semicolon-separated one");
        Check(GraphMailboxes.Parse("a@x.co.th\nb@x.co.th").Count == 2, "and one written on two lines");
        Check(GraphMailboxes.Parse("  a@x.co.th ,, b@x.co.th  ").Count == 2, "spaces and empty entries are dropped");

        Check(GraphMailboxes.Parse("A@X.co.th, a@x.CO.TH").Count == 1,
            "one mailbox spelled two ways is one mailbox");
        Check(GraphMailboxes.Parse("ops@x.co.th")[0] == "ops@x.co.th", "and it is stored lower-cased");

        // The whole point of the second fence. Reading this as "no list means no
        // restriction" is how every mailbox in the tenant becomes readable.
        Check(GraphMailboxes.Parse(null).Count == 0, "an unset setting is an empty list");
        Check(GraphMailboxes.Parse("   ").Count == 0, "and so is a blank one");

        Console.WriteLine();
        Console.WriteLine("And what that list refuses.");
        Console.WriteLine();

        Check(GraphMailboxes.IsApproved("ops@leschaco.co.th", list), "an approved mailbox is approved");
        Check(GraphMailboxes.IsApproved("OPS@Leschaco.CO.TH", list), "however it is capitalised");
        Check(GraphMailboxes.IsApproved("  ops@leschaco.co.th  ", list), "and however it is padded");

        Check(!GraphMailboxes.IsApproved("ceo@leschaco.co.th", list), "a mailbox not on the list is refused");
        Check(!GraphMailboxes.IsApproved("", list), "an empty address is refused");
        Check(!GraphMailboxes.IsApproved(null, list), "and so is none at all");

        // Mail.Read as an application permission reads the whole tenant. An
        // empty list must therefore approve nothing, not everything.
        Check(!GraphMailboxes.IsApproved("ops@leschaco.co.th", []),
            "an empty list approves nothing — not everything");

        // A list built by hand rather than by Parse still compares correctly.
        Check(GraphMailboxes.IsApproved("ops@x.co.th", [" OPS@X.CO.TH "]),
            "a list that never went through Parse still matches");

        Console.WriteLine();
        Console.WriteLine("What is reported back as not looking like a mailbox.");
        Console.WriteLine();

        Check(GraphMailboxes.LooksLikeAddress("ops@leschaco.co.th"), "an address looks like one");
        Check(!GraphMailboxes.LooksLikeAddress("leschaco.co.th"), "a domain alone does not");
        Check(!GraphMailboxes.LooksLikeAddress("ops@"), "nor a name with nothing after the @");
        Check(!GraphMailboxes.LooksLikeAddress("@leschaco.co.th"), "nor a domain with nothing before it");
        Check(!GraphMailboxes.LooksLikeAddress("a@b@c.co.th"), "nor two @ signs");
        Check(!GraphMailboxes.LooksLikeAddress("ops@localhost"), "nor a domain with no dot");
        Check(!GraphMailboxes.LooksLikeAddress("ops @x.co.th"), "nor anything with a space in it");

        // The reason this exists at all: somebody will try to approve a domain.
        Check(!GraphMailboxes.LooksLikeAddress("*@leschaco.co.th"),
            "a wildcard is reported, not silently matched against nothing");

        Console.WriteLine();
        Console.WriteLine("What Entra says it granted, read off the token.");
        Console.WriteLine();

        var granted = Jwt("{\"aud\":\"https://graph.microsoft.com\",\"roles\":[\"Mail.Read\"]}");
        Check(GraphToken.Grants(granted, GraphAuth.MailRead), "a token carrying Mail.Read grants it");
        Check(!GraphToken.Grants(granted, "Mail.Send"), "and does not grant anything else");

        var several = Jwt("{\"roles\":[\"User.ReadWrite.All\",\"Mail.Read\"]}");
        Check(GraphToken.RolesIn(several).Count == 2, "two permissions are both read");
        Check(GraphToken.Grants(several, "Mail.Read"), "and the second one is found");

        Check(GraphToken.Grants(Jwt("{\"roles\":[\"mail.read\"]}"), "Mail.Read"),
            "a permission is matched however Entra capitalises it");

        // The state this integration is actually in today: the identity works,
        // the consent has not been given. It must read as "no", not as a fault.
        Check(!GraphToken.Grants(Jwt("{\"aud\":\"https://graph.microsoft.com\"}"), "Mail.Read"),
            "a token with no roles claim grants nothing");
        Check(GraphToken.RolesIn(Jwt("{\"roles\":[]}")).Count == 0, "an empty roles claim is no permissions");

        Console.WriteLine();
        Console.WriteLine("And what it does with anything it cannot read.");
        Console.WriteLine();

        // Never throws: this runs while reporting why Graph is not working, and
        // a diagnostic that crashes takes the diagnosis with it.
        Check(GraphToken.RolesIn(null).Count == 0, "no token at all is no permissions");
        Check(GraphToken.RolesIn("").Count == 0, "an empty string is no permissions");
        Check(GraphToken.RolesIn("not-a-token").Count == 0, "so is something with no dots in it");
        Check(GraphToken.RolesIn("a.b.c").Count == 0, "so is a token whose payload is not base64");
        Check(GraphToken.RolesIn(Jwt("not json")).Count == 0, "so is one whose payload is not JSON");
        Check(GraphToken.RolesIn(Jwt("{\"roles\":\"Mail.Read\"}")).Count == 0,
            "and a roles claim that is a string rather than a list is ignored");
        Check(GraphToken.RolesIn(Jwt("{\"roles\":[1,2]}")).Count == 0, "as are permissions that are not names");

        // Base64url drops the '=' padding, so the decoder has to put it back.
        // Payloads are grown one character at a time to hit every remainder;
        // getting this wrong fails on some tokens and not others, which is the
        // worst way for it to fail.
        var everyPadding = true;
        for (var pad = 0; pad < 6; pad++)
        {
            var filler = new string('x', pad);
            var token = Jwt($"{{\"sub\":\"{filler}\",\"roles\":[\"Mail.Read\"]}}");
            if (!GraphToken.Grants(token, "Mail.Read")) everyPadding = false;
        }
        Check(everyPadding, "a payload of any length decodes — every base64url padding case");

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "All Graph authentication checks passed."
            : $"{failed} Graph authentication check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
