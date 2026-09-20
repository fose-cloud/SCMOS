using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Scmos.Api.Rules;

/// <summary>
/// Webhooks to a carrier's TMS — Phase 4 of the Carrier TMS API (20 Sep
/// 2026): what SCMOS tells a carrier's system without being asked, and
/// the rules of the telling.
///
/// <para>
/// A TMS registers a URL and the events it wants; SCMOS POSTs each event
/// once, signed with a secret shown once at registration, and retries on
/// a schedule that stretches from a minute to half a day before it gives
/// the delivery up as dead. The URL is the carrier's choice, so it is
/// held to https and to a public host: SCMOS's server will not be pointed
/// at itself, at the database, or at anything on the private network.
/// </para>
///
/// <para>Pure: URLs, signatures and the schedule. <c>--check-carrier-api</c> proves them.</para>
/// </summary>
public static class CarrierWebhooks
{
    /// <summary>A carrier was asked for a truck: a request now waits for its answer.</summary>
    public const string Offered = "assignment.offered";

    /// <summary>The request is withdrawn — another carrier took the job, or the operator cancelled the ask.</summary>
    public const string Cancelled = "assignment.cancelled";

    /// <summary>A status event the TMS queued was approved or set aside by the job's owner.</summary>
    public const string EventDecided = "event.decided";

    /// <summary>A test delivery, sent on request.</summary>
    public const string Ping = "ping";

    /// <summary>The events a webhook may subscribe to; a ping goes to every webhook that asks for one.</summary>
    public static readonly string[] Types = [Offered, Cancelled, EventDecided];

    public const string Active = "active";
    public const string Disabled = "disabled";

    public const string Pending = "pending";
    public const string Delivered = "delivered";
    public const string Dead = "dead";

    /// <summary>The prefix of a signing secret, so one in the wrong place is recognisable.</summary>
    public const string SecretPrefix = "whsec_";

    /// <summary>How long SCMOS waits for the TMS to answer a delivery.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The wait before each retry: a minute, five, half an hour, two hours,
    /// twelve — six attempts in all across about fifteen hours, which
    /// covers a TMS that is down for the night without SCMOS hammering it.
    /// </summary>
    public static readonly TimeSpan[] Delays =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12)];

    public static int MaxAttempts => Delays.Length + 1;

    /// <summary>How many webhooks one supplier may hold — one per system, with room for a test one.</summary>
    public const int MaxPerSupplier = 5;

    /// <summary>
    /// Failed attempts in a row, with none delivered, after which SCMOS
    /// stops calling the webhook and disables it: thirty is at least five
    /// deliveries dead, which needs at least one to have run its whole
    /// schedule — fifteen hours of a receiver that never once answered
    /// 2xx. A carrier that has mended its receiver registers again.
    /// </summary>
    public const int RetiresAfter = 30;

    /// <summary>Whether a webhook with this many failures in a row is retired on this failure.</summary>
    public static bool Retires(int failedInARow) => failedInARow >= RetiresAfter;

    /// <summary>What the retired webhook's row says.</summary>
    public static string RetiredReason(int failedInARow) => $"disabled by SCMOS: {failedInARow} deliveries failed in a row";

    /// <summary>The events out of a body — case forgiven, duplicates dropped; empty means all three. Null when one is not an event.</summary>
    public static IReadOnlyList<string>? ReadEvents(IEnumerable<string?>? wanted)
    {
        var list = new List<string>();
        foreach (var one in wanted ?? [])
        {
            var text = (one ?? "").Trim().ToLowerInvariant();
            if (text.Length == 0) continue;
            if (!Types.Contains(text, StringComparer.Ordinal)) return null;
            if (!list.Contains(text)) list.Add(text);
        }
        return list.Count == 0 ? Types : list;
    }

    /// <summary>The events as the row stores them, and back.</summary>
    public static string JoinEvents(IReadOnlyList<string> events) => string.Join(",", events);
    public static IReadOnlyList<string> SplitEvents(string stored) =>
        stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Whether a webhook subscribed to an event; a ping reaches every webhook.</summary>
    public static bool Wants(string storedEvents, string type) =>
        type == Ping || SplitEvents(storedEvents).Contains(type, StringComparer.Ordinal);

    /// <summary>
    /// Why a URL cannot be a webhook, or null when it can: absolute, https
    /// (http only where <paramref name="allowInsecure"/> — a developer's
    /// machine), no credentials in it, at most 500 characters, and a host
    /// that is not this machine, a loopback, a link-local or a private
    /// address — SCMOS's server is not to be pointed at the inside.
    /// </summary>
    public static string? UrlProblem(string? text, bool allowInsecure = false)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0) return "url is required";
        if (value.Length > 500) return "url is longer than 500 characters";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "url must be absolute";
        if (uri.Scheme != Uri.UriSchemeHttps && !(allowInsecure && uri.Scheme == Uri.UriSchemeHttp)) return "url must be https";
        if (uri.UserInfo.Length > 0) return "url must not carry credentials";
        if (uri.Fragment.Length > 0) return "url must not carry a fragment";
        var host = uri.Host.ToLowerInvariant();
        var literal = IPAddress.TryParse(host.Trim('[', ']'), out var address);
        if (!allowInsecure)
        {
            if (host is "localhost" || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal)
                || !host.Contains('.'))
                return "url must name a public host";
        }
        // A developer's machine may be called on its loopback; nothing may
        // be pointed at the private network, anywhere.
        if (literal && IsPrivate(address!) && !(allowInsecure && IPAddress.IsLoopback(address!)))
            return "url must not point at a private address";
        return null;
    }

    /// <summary>
    /// Why a delivery must not connect, or null when it may: the host the
    /// URL names resolved to these addresses at the moment of sending, and
    /// a name that resolves to a private address — a rebind, a split
    /// horizon, a mistake — is refused whole; the URL check at
    /// registration only saw the name. The loopback is allowed only where
    /// insecure URLs are (a developer's machine).
    /// </summary>
    public static string? AddressProblem(IReadOnlyList<IPAddress> addresses, bool allowLoopback = false)
    {
        if (addresses.Count == 0) return "host resolves to no address";
        foreach (var address in addresses)
        {
            if (allowLoopback && IPAddress.IsLoopback(address)) continue;
            if (IsPrivate(address)) return $"host resolves to a private address ({address})";
        }
        return null;
    }

    /// <summary>Loopback, link-local, RFC 1918, carrier-grade NAT, unspecified, multicast — the addresses a server must not be sent to.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            || b[0] == 0
            || b[0] >= 224;
    }

    /// <summary>A new signing secret: the prefix and 32 random bytes, base64url.</summary>
    public static string NewSecret() =>
        SecretPrefix + System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// The signature a delivery carries: "sha256=" and the HMAC-SHA256, in
    /// lower-case hex, of the timestamp, a dot and the body, under the
    /// webhook's secret. The timestamp is inside the signature so a
    /// captured delivery cannot be replayed later as a fresh one.
    /// </summary>
    public static string Signature(string secret, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "." + body));
        return "sha256=" + Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>Whether a signature is the one the secret gives — in constant time, for the TMS side of the check and for our own test.</summary>
    public static bool Verify(string secret, string timestamp, string body, string? signature)
    {
        var want = Encoding.UTF8.GetBytes(Signature(secret, timestamp, body));
        var got = Encoding.UTF8.GetBytes(signature ?? "");
        return want.Length == got.Length && CryptographicOperations.FixedTimeEquals(want, got);
    }

    /// <summary>When to try again after the <paramref name="attempts"/>th failure, or null when the delivery is dead.</summary>
    public static DateTimeOffset? NextAttempt(int attempts, DateTimeOffset now) =>
        attempts >= 1 && attempts <= Delays.Length ? now + Delays[attempts - 1] : null;

    /// <summary>A 2xx is a delivery; anything else, or no answer, is a failure to retry.</summary>
    public static bool IsDelivered(int statusCode) => statusCode is >= 200 and < 300;

    /// <summary>The part of a secret a person may be shown afterwards.</summary>
    public static string ShownPrefixOf(string secret) =>
        secret.Length >= SecretPrefix.Length + 4 ? secret[..(SecretPrefix.Length + 4)] + "…" : "";
}
