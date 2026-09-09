using System.Security.Cryptography;
using System.Text;

namespace Scmos.Api.Rules;

/// <summary>
/// Keeping a Graph change notification alive, and knowing when it is not.
///
/// <para>
/// Graph expires a mail subscription in under three days and <b>stops
/// delivering without saying so</b>. A subscription that lapsed unnoticed is a
/// mailbox that has quietly stopped being read, which looks exactly like a
/// mailbox nobody has written to — the two are indistinguishable from the
/// outside, and that is the failure this whole file exists to prevent.
/// </para>
///
/// <para>
/// So the expiry is stored and renewed against rather than assumed, and the
/// decision of what to do about a given subscription is here, pure, where
/// <c>--check-subscriptions</c> can walk a clock past it. The loop that acts on
/// the decision cannot be checked without Graph; the decision can.
/// </para>
/// </summary>
public static class GraphSubscriptions
{
    /// <summary>
    /// How long a subscription is asked to live.
    ///
    /// <para>
    /// Graph caps Outlook resources at 4,230 minutes — just under three days —
    /// and <b>rejects the request outright</b> if asked for more, rather than
    /// quietly giving less. 4,200 leaves half an hour for the clocks at either
    /// end to disagree, which costs nothing: the renewal window below is
    /// twenty-four hours wide.
    /// </para>
    /// </summary>
    public const int LifetimeMinutes = 4200;

    /// <summary>
    /// How early a renewal is attempted.
    ///
    /// A whole day before expiry, against an hourly loop, means roughly
    /// twenty-four chances to renew before delivery stops. The container is
    /// recycled whenever App Service likes and Graph is occasionally busy;
    /// anything tighter turns one bad hour into a mailbox that goes quiet.
    /// </summary>
    public static readonly TimeSpan RenewWithin = TimeSpan.FromHours(24);

    /// <summary>What the loop should do about one subscription.</summary>
    public static class Do
    {
        /// <summary>Nothing. It is alive and not near its end.</summary>
        public const string Leave = "LEAVE";

        /// <summary>Extend it. It is alive, and inside the renewal window.</summary>
        public const string Renew = "RENEW";

        /// <summary>Make a new one. This one is gone, or Graph has disowned it.</summary>
        public const string Recreate = "RECREATE";
    }

    /// <summary>
    /// What to do about a subscription, given its state and the time.
    ///
    /// <para>
    /// Expiry is checked before status on purpose. A row still marked ACTIVE
    /// whose expiry has passed is the ordinary case — Graph does not tell
    /// anybody it has stopped, so the row goes on claiming to be active until
    /// something looks at the clock. Believing the status over the clock is how
    /// a mailbox goes quiet for three days.
    /// </para>
    /// </summary>
    public static string Due(string? status, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        // Already over. Renewing an expired subscription is not a thing Graph
        // offers — it has to be created again.
        if (expiresAt <= now) return Do.Recreate;

        // Anything Graph has told us about, or that failed, is not going to
        // start working again by being left alone.
        if (!string.Equals(status, Data.MailSubscription.Active, StringComparison.Ordinal))
            return Do.Recreate;

        return expiresAt - now <= RenewWithin ? Do.Renew : Do.Leave;
    }

    /// <summary>When to ask Graph to expire it, from now.</summary>
    public static DateTimeOffset ExpiryFrom(DateTimeOffset now) =>
        now.AddMinutes(LifetimeMinutes);

    /// <summary>
    /// What the subscription watches.
    ///
    /// <para>
    /// The Graph user id is preferred over the address wherever it is known,
    /// which is the reason <c>Mailbox.GraphUserId</c> is stored beside the
    /// address at all: an address can be reassigned and an id cannot, so a
    /// subscription renewed against the address alone would follow the name to
    /// whoever holds it next — and go on delivering somebody else's mail into
    /// this register without anything looking wrong.
    /// </para>
    /// </summary>
    public static string ResourceFor(string? graphUserId, string? address, string? folderId)
    {
        var who = string.IsNullOrWhiteSpace(graphUserId)
            ? GraphMailboxes.Normalise(address)
            : graphUserId.Trim();
        if (who.Length == 0) return "";

        var folder = (folderId ?? "").Trim();
        return folder.Length == 0
            ? $"users/{who}/messages"
            : $"users/{who}/mailFolders/{folder}/messages";
    }

    /// <summary>
    /// A new shared secret for one subscription.
    ///
    /// <para>
    /// Graph echoes this back on every notification, and it is the only thing
    /// that distinguishes a genuine delivery from anybody who found the URL —
    /// the webhook has to be reachable from Microsoft's network, so it is
    /// reachable from everybody's. 256 bits from the cryptographic generator,
    /// not <c>Guid.NewGuid</c>, which is a unique value rather than an
    /// unguessable one.
    /// </para>
    ///
    /// <para>
    /// One per subscription rather than one for the deployment, so a leak from
    /// one mailbox does not authenticate deliveries for another.
    /// </para>
    /// </summary>
    public static string NewClientState() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Whether the secret Graph sent back is the one we stored.
    ///
    /// <para>
    /// Fixed-time, for the reason <see cref="LineSignature"/> is: ordinary
    /// string equality returns as soon as two bytes differ, and how long it took
    /// says how much of the prefix was right. That is enough to recover the
    /// value a byte at a time against an endpoint anybody can call as often as
    /// they like.
    /// </para>
    ///
    /// <para>
    /// An empty stored secret is false, never true. A subscription with no
    /// secret would otherwise accept every delivery from anybody the day a row
    /// was written badly.
    /// </para>
    /// </summary>
    public static bool StateMatches(string? stored, string? sent)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(sent)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(sent));
    }

    /// <summary>
    /// Whether Graph could actually deliver to this address.
    ///
    /// <para>
    /// Checked before a subscription is created rather than after Graph refuses
    /// it, because the refusal is a generic validation error and the cause —
    /// somebody left a localhost URL in an app setting — is not in it.
    /// Microsoft's network has to reach this, so anything that only resolves
    /// from inside is not a notification URL, it is a value that was never
    /// changed.
    /// </para>
    /// </summary>
    public static bool IsDeliverable(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target)) return false;
        if (target.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(target.UserInfo)) return false;

        var host = target.Host;
        if (host.Length == 0) return false;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return false;
        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return false;
        return !System.Net.IPAddress.TryParse(host, out var literal)
            || !(System.Net.IPAddress.IsLoopback(literal) || IsPrivate(literal));
    }

    /// <summary>The address ranges the internet does not route to.</summary>
    private static bool IsPrivate(System.Net.IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            10 => true,
            172 => octets[1] >= 16 && octets[1] <= 31,
            192 => octets[1] == 168,
            169 => octets[1] == 254,
            _ => false,
        };
    }
}
