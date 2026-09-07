using System.Security.Cryptography;
using System.Text;

namespace Scmos.Api.Rules;

/// <summary>
/// Proving a webhook really came from LINE.
///
/// <para>
/// LINE signs each delivery with an HMAC-SHA256 of the <b>raw request body</b>
/// under the channel secret, base64 encoded, in the <c>x-line-signature</c>
/// header. Anything that fails this is not from LINE and is refused before the
/// database is touched — the endpoint is open to the internet by necessity, and
/// this is the whole of its authentication.
/// </para>
///
/// <para>
/// Three things here are load-bearing and easy to get wrong.
/// </para>
///
/// <para>
/// <b>The raw body.</b> Not the model-bound object, not a re-serialised copy.
/// Round-tripping JSON through a parser changes whitespace and key order, and
/// the signature is over the bytes that arrived. The endpoint reads the stream.
/// </para>
///
/// <para>
/// <b>Fixed-time comparison.</b> An ordinary string equality returns as soon as
/// two bytes differ, and the time it took says how much of the prefix was
/// right. That is enough to recover a signature a byte at a time over enough
/// attempts, against an endpoint anybody can call as often as they like.
/// </para>
///
/// <para>
/// <b>No secret in any message.</b> Everything here answers true or false. A
/// caller that logs the reason cannot accidentally log the key.
/// </para>
///
/// <para>
/// Pure, so <c>--check-line</c> can prove it against known vectors with no
/// network and no LINE account.
/// </para>
/// </summary>
public static class LineSignature
{
    /// <summary>
    /// The signature LINE would send for this body under this secret.
    ///
    /// Exposed so the check harness can build a valid header and prove the
    /// verifier accepts it — a verifier only ever tested against rejections is
    /// one that might reject everything.
    /// </summary>
    public static string Sign(string secret, string rawBody)
    {
        using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? ""));
        return Convert.ToBase64String(mac.ComputeHash(Encoding.UTF8.GetBytes(rawBody ?? "")));
    }

    /// <summary>
    /// Whether a delivery is genuine.
    ///
    /// False for every doubt: no secret configured, no header, a header that is
    /// not base64, a body that does not match. The caller answers 403 and says
    /// nothing more — telling an unauthenticated caller *why* it failed is
    /// telling them how to get closer.
    /// </summary>
    public static bool Verify(string? secret, string? header, string? rawBody)
    {
        // An unconfigured secret must never pass. It would turn the endpoint
        // into one that accepts anything the day somebody forgets an app
        // setting, which is exactly when nobody is looking.
        if (string.IsNullOrEmpty(secret)) return false;
        if (string.IsNullOrEmpty(header)) return false;
        if (rawBody is null) return false;

        byte[] sent;
        try
        {
            sent = Convert.FromBase64String(header);
        }
        catch (FormatException)
        {
            return false;
        }

        using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var mine = mac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));

        // Length is checked by FixedTimeEquals itself, which returns false for a
        // mismatch without an early exit.
        return CryptographicOperations.FixedTimeEquals(sent, mine);
    }
}
