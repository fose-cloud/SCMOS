using System.Security.Cryptography;
using System.Text;

namespace Scmos.Api.Rules;

/// <summary>
/// The Carrier TMS API, V1 — the parts that decide something and touch no
/// database: what a key looks like and how it is stored, what a correlation
/// id may be, how a date window and a page are read, and what each refusal
/// is called. Phase 1 of the Carrier TMS Integration specification
/// (<c>docs/integrations/carrier-tms/</c>), 20 September 2026.
///
/// <para>
/// A carrier's TMS is a machine, and SCMOS has never had a machine caller
/// with an identity of its own: people arrive through Entra and the proxy
/// key, LINE arrives with an HMAC. This is the third door. The credential is
/// an SCMOS-issued key; the register stores only its SHA-256, the way a
/// password is stored, so a copy of the table is not a copy of the keys. The
/// key is high-entropy (32 random bytes), which is why a fast hash is
/// enough — there is nothing to guess. Which carrier a key speaks for is a
/// column on the key's row, resolved on the server, never read from the
/// request (<c>CarrierService</c> already works this way for a person's
/// account).
/// </para>
///
/// <para>Pure: a value in, a verdict out. <c>--check-carrier-api</c> proves it.</para>
/// </summary>
public static class CarrierApi
{
    /// <summary>What every key starts with, so one in a log or a chat is recognisable at a glance and revocable.</summary>
    public const string KeyPrefix = "scmos_ck_";

    /// <summary>Requests a minute a keyed caller may make; a TMS polling faster than this is keeping the serverless database awake for nothing.</summary>
    public const int RequestsPerMinute = 120;

    /// <summary>Requests a minute an address with no key, or a bad one, may make: enough to notice a typo, not enough to guess.</summary>
    public const int AnonymousRequestsPerMinute = 20;

    /// <summary>The header a caller may set to tie SCMOS's answer to its own log; echoed back, and written on every audit row the call makes.</summary>
    public const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>The two groups an assignment is in, from the carrier's side of the table.</summary>
    public const string Offered = "offered";
    public const string Accepted = "accepted";

    /* ------------------------------------------------ idempotency (phase 2) */

    /// <summary>
    /// The header every write must carry. A TMS that times out and retries
    /// sends the same key, and gets the same answer back instead of a
    /// second acceptance; the same key with a different body is refused,
    /// since it cannot be the same request.
    /// </summary>
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>Set on a replayed answer, so the caller can tell a stored answer from a fresh one.</summary>
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>How long a stored answer is kept for replay.</summary>
    public const int IdempotencyDays = 30;

    /// <summary>The key as the caller sent it, when it is one to 128 printable ASCII characters; otherwise null.</summary>
    public static string? IdempotencyKeyOf(string? header)
    {
        var text = (header ?? "").Trim();
        if (text.Length is < 1 or > 128) return null;
        foreach (var c in text)
        {
            if (c is < '!' or > '~') return null;
        }
        return text;
    }

    /// <summary>What a request is, for telling a retry from a reuse: the method, the path and the body, hashed.</summary>
    public static string RequestHash(string method, string path, string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(method.ToUpperInvariant() + "\n" + path + "\n" + body))).ToLowerInvariant();

    /* ----------------------------------------------- the truck (phase 2) */

    /// <summary>
    /// Whether text is one plate, with or without its province — the
    /// parser's plate rule, which is stricter than the register's cell rule
    /// (that one accepts "1500", because it is checking a cell a person
    /// keyed, not reading a machine's field).
    /// </summary>
    public static bool IsPlate(string text)
    {
        var found = LineParser.FindPlates(LineParser.Normalise(text));
        return found.Count == 1 && LineParser.PlateKey(found[0]) == LineParser.PlateKey(text);
    }

    /// <summary>The truck's details as the register will hold them — each cleaned to the data standard, empty when not sent.</summary>
    public sealed record TruckInput(string Licence, string Driver, string Contact, string Container, string Seal)
    {
        public bool IsEmpty => Licence.Length == 0 && Driver.Length == 0 && Contact.Length == 0 && Container.Length == 0 && Seal.Length == 0;
    }

    /// <summary>
    /// The truck's details out of a body, held to the register's standard:
    /// a plate the register would accept, a phone written back as
    /// 0XX-XXXXXXX, a container of four letters and seven digits (a check
    /// digit that disagrees is a warning, not a refusal — the register may
    /// carry the same number from the booking), a seal of at most forty
    /// characters. <paramref name="requireTruck"/> is the acceptance's rule:
    /// plate, driver and number all present, the way the portal insists.
    /// </summary>
    public static (TruckInput Fields, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings) ReadTruck(
        string? licence, string? driver, string? contact, string? container, string? seal, bool requireTruck)
    {
        var problems = new List<string>();
        var warnings = new List<string>();

        var plate = Formats.Clean(licence);
        if (plate.Length == 0 && requireTruck) problems.Add("licence is required");
        else if (plate.Length > 0 && !IsPlate(plate)) problems.Add("licence is not a Thai licence plate (e.g. 70-1234 or 1กข-1234, province optional)");

        var name = Formats.Clean(driver);
        if (name.Length == 0 && requireTruck) problems.Add("driver is required");
        else if (name.Length > 80) problems.Add("driver is longer than 80 characters");

        var phone = Formats.Clean(contact);
        if (phone.Length == 0 && requireTruck) problems.Add("contact is required");
        else if (phone.Length > 0)
        {
            var read = LineParser.FindPhone(LineParser.Normalise(phone));
            if (read is null) problems.Add("contact is not a Thai phone number (e.g. 081-2345678)");
            else phone = read;
        }

        var box = ContainerNumbers.Normalise(container);
        if (box.Length > 0)
        {
            if (!ContainerNumbers.IsShaped(box)) problems.Add("container must be four letters (the fourth U, J or Z) and seven digits");
            else if (!ContainerNumbers.IsValid(box)) warnings.Add($"container {box}: the ISO 6346 check digit does not agree; written as sent");
        }

        var sealNo = Formats.Clean(seal);
        if (sealNo.Length > 40) problems.Add("seal is longer than 40 characters");

        return (new TruckInput(plate, name, phone, box, sealNo), problems, warnings);
    }

    /// <summary>How far a window may reach — a quarter, which is what a reconciliation asks for.</summary>
    public const int MaxWindowDays = 92;

    /// <summary>Rows a page may carry; the register is read whole and paged after, so this caps the answer, not the work.</summary>
    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 50;

    /// <summary>A new key: the prefix and 32 random bytes, base64url, 52 characters in all.</summary>
    public static string NewKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return KeyPrefix + System.Buffers.Text.Base64Url.EncodeToString(bytes);
    }

    /// <summary>A client's public name — "ck_" and ten letters or digits — for logs, the screen and the ledger; never a secret.</summary>
    public static string NewClientId()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(10);
        var built = new StringBuilder("ck_");
        foreach (var b in bytes) built.Append(alphabet[b % alphabet.Length]);
        return built.ToString();
    }

    /// <summary>Whether text has the shape of a key: the prefix and exactly the 43 base64url characters 32 bytes make.</summary>
    public static bool IsKeyShaped(string? key)
    {
        if (key is null || !key.StartsWith(KeyPrefix, StringComparison.Ordinal)) return false;
        var body = key[KeyPrefix.Length..];
        if (body.Length != 43) return false;
        foreach (var c in body)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return false;
        }
        return true;
    }

    /// <summary>What the register keeps of a key: its SHA-256, lower-case hex. Never the key.</summary>
    public static string HashOf(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    /// <summary>The part of a key a person may be shown afterwards — the prefix and four characters — enough to tell two apart, not enough to use.</summary>
    public static string ShownPrefixOf(string key) =>
        key.Length >= KeyPrefix.Length + 4 ? key[..(KeyPrefix.Length + 4)] + "…" : "";

    /// <summary>
    /// The key out of an Authorization header — "Bearer scmos_ck_…" — or null.
    /// Only the bearer scheme, only a shaped key: anything else is not a
    /// credential and is not looked up.
    /// </summary>
    public static string? KeyFrom(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization)) return null;
        var text = authorization.Trim();
        if (!text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var key = text["Bearer ".Length..].Trim();
        return IsKeyShaped(key) ? key : null;
    }

    /// <summary>
    /// The correlation id for a request: the caller's, when it is one to
    /// sixty-four characters of letters, digits, dot, dash or underscore;
    /// otherwise a fresh one. A caller's id is copied, never trusted with
    /// anything but its own shape — it goes into logs and audit rows.
    /// </summary>
    public static string CorrelationId(string? header)
    {
        var text = (header ?? "").Trim();
        if (text.Length is >= 1 and <= 64 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            return text;
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// The rate limiter's bucket for a request: the key's hash when one is
    /// presented (so one carrier's TMS cannot spend another's allowance, and
    /// a good key and a bad one from the same address are told apart), else
    /// the address. The bucket names its allowance by its prefix.
    /// </summary>
    public static string PartitionOf(string? authorization, string? remoteAddress)
    {
        var key = KeyFrom(authorization);
        return key is not null ? "key:" + HashOf(key)[..16] : "ip:" + (remoteAddress ?? "unknown");
    }

    public static int AllowanceOf(string partition) =>
        partition.StartsWith("key:", StringComparison.Ordinal) ? RequestsPerMinute : AnonymousRequestsPerMinute;

    /* ------------------------------------------------------- the window */

    /// <summary>
    /// The work-date window a listing covers: <c>from</c> and <c>to</c> as
    /// yyyy-MM-dd or dd/MM/yyyy, inclusive; a week back and two weeks ahead
    /// of today when absent. Null when a bound cannot be read, the end is
    /// before the start, or the span is longer than <see cref="MaxWindowDays"/>.
    /// </summary>
    public static (DateOnly From, DateOnly To)? Window(string? from, string? to, DateOnly today)
    {
        var start = from is null ? today.AddDays(-7) : ReadDay(from);
        var end = to is null ? today.AddDays(14) : ReadDay(to);
        if (start is null || end is null) return null;
        if (end.Value < start.Value) return null;
        if (end.Value.DayNumber - start.Value.DayNumber > MaxWindowDays) return null;
        return (start.Value, end.Value);
    }

    /// <summary>A day written either way the API accepts, or null.</summary>
    public static DateOnly? ReadDay(string text)
    {
        var value = text.Trim();
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var iso))
            return iso;
        var (year, month, day) = Formats.PartsOf(value);
        return year.Length > 0 && DateOnly.TryParseExact($"{year}-{month}-{day}", "yyyy-M-d",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var slashed)
            ? slashed
            : null;
    }

    /// <summary>Whether a job's DATE cell, as the register writes it, falls inside the window. A cell that is not a date is outside every window.</summary>
    public static bool InWindow(string? dateCell, DateOnly from, DateOnly to)
    {
        var number = Formats.DateNumber(dateCell);
        if (number == 0) return false;
        return number >= Number(from) && number <= Number(to);
    }

    private static int Number(DateOnly day) => day.Year * 10000 + day.Month * 100 + day.Day;

    /// <summary>The page asked for, held to what the API serves: page one or later, one to two hundred rows, fifty unless said.</summary>
    public static (int Page, int PageSize) Page(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));

    /// <summary>Which of the two groups a listing asks for, or null for both; anything else is a refusal.</summary>
    public static bool TryGroup(string? status, out string? group)
    {
        group = null;
        if (string.IsNullOrWhiteSpace(status)) return true;
        var text = status.Trim().ToLowerInvariant();
        if (text is Offered or Accepted) { group = text; return true; }
        return false;
    }

    /* ------------------------------------------------------ the refusals */

    /// <summary>
    /// What a refusal is called — RFC 9457 Problem Details, one vocabulary.
    /// The <c>type</c> is a URN, stable across releases, so a TMS can switch
    /// on it; the title is for a person; the detail is the sentence for this
    /// call. Never a stack trace, never another carrier's existence.
    /// </summary>
    public sealed record Problem(string Code, string Type, string Title, int Status);

    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not-found";
    public const string Invalid = "invalid-request";
    public const string Conflict = "conflict";
    public const string RateLimited = "rate-limited";
    public const string Unavailable = "unavailable";
    /// <summary>The same Idempotency-Key with a different request — not a retry, a mistake.</summary>
    public const string KeyReused = "idempotency-key-reused";
    /// <summary>The same request is still being answered; try again in a moment.</summary>
    public const string InProgress = "in-progress";

    public static Problem ProblemOf(string code) => code switch
    {
        Unauthorized => new(code, TypeOf(code), "The request carried no valid API key", 401),
        Forbidden => new(code, TypeOf(code), "This key may not do that", 403),
        NotFound => new(code, TypeOf(code), "No such assignment for this carrier", 404),
        Invalid => new(code, TypeOf(code), "The request could not be read", 400),
        Conflict => new(code, TypeOf(code), "The assignment is not in a state that allows this", 409),
        RateLimited => new(code, TypeOf(code), "Too many requests", 429),
        KeyReused => new(code, TypeOf(code), "The Idempotency-Key was already used for a different request", 422),
        InProgress => new(code, TypeOf(code), "The same request is still being processed", 409),
        _ => new(Unavailable, TypeOf(Unavailable), "SCMOS could not answer", 503),
    };

    public static string TypeOf(string code) => $"urn:scmos:carrier-api:{code}";
}
