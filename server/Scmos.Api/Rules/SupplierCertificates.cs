namespace Scmos.Api.Rules;

/// <summary>
/// A carrier's management-system certificates — Q-Mark and the ISO standards (2 Oct 2026, Annual Evaluation Phase 11).
/// Pure.
///
/// <para>
/// The register had nowhere to keep them, so the evaluation said "not available". The department's own 2025 table says,
/// carrier by carrier, which it holds — "มี" or "ไม่มี", no number, no date, no copy. That is recorded as it is: a
/// <see cref="Declared"/> certificate, held by the department's word, which a certificate seen and dated later replaces
/// as <see cref="Verified"/>. Nobody is taken to be certified without a record of it, and a declaration is never shown as a
/// valid certificate.
/// </para>
/// </summary>
public static class SupplierCertificates
{
    /// <summary>The certificates the department tracks, in its order. ISO 39001 is road-traffic safety.</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> Types =
    [
        ("q-mark", "Q-Mark"), ("iso-9001", "ISO 9001"), ("iso-14001", "ISO 14001"), ("iso-39001", "ISO 39001"), ("iso-45001", "ISO 45001"),
    ];

    public static bool IsType(string? code) => Types.Any(type => type.Code == code);

    public static string LabelOf(string code) => Types.FirstOrDefault(type => type.Code == code).Label ?? code;

    /* ---- how a record was made ---- */
    public const string Declared = "declared";
    public const string Verified = "verified";
    public const string Manual = "manual";
    public const string LegacyImport = "legacy-import";

    /* ---- where a certificate stands ---- */
    public const string NotHeld = "not-held";

    /// <summary>
    /// valid · expiring · expired when an expiry is known (the register's own rule, <see cref="SupplierCompliance.StateOf"/>);
    /// otherwise <see cref="Declared"/> for one held by somebody's word and <c>no-expiry</c> for one seen without a date;
    /// <see cref="NotHeld"/> when the carrier does not hold it.
    /// </summary>
    public static string StateOf(bool held, string verification, string expiresOn, int today)
    {
        if (!held) return NotHeld;
        if (Formats.DateNumber(expiresOn ?? "") > 0) return SupplierCompliance.StateOf(true, expiresOn ?? "", today);
        return verification == Verified ? SupplierCompliance.State.NoExpiry : Declared;
    }

    /// <summary>Whether a certificate in this state is one the carrier holds now.</summary>
    public static bool Holds(string state) =>
        state is SupplierCompliance.State.Valid or SupplierCompliance.State.Expiring or SupplierCompliance.State.NoExpiry or Declared;

    /// <summary>The certificate a column heading names — "ISO 14000" is the family the department wrote for ISO 14001.</summary>
    public static string? TypeOfHeading(string heading)
    {
        var key = new string((heading ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return key switch
        {
            "qmark" => "q-mark",
            "iso9001" or "iso9000" => "iso-9001",
            "iso14001" or "iso14000" => "iso-14001",
            "iso39001" => "iso-39001",
            "iso45001" => "iso-45001",
            _ => null,
        };
    }

    /// <summary>Whether a cell says the certificate is held: มี / ไม่มี as the department writes it, or yes / no. Null when it says neither.</summary>
    public static bool? HeldOf(string? cell)
    {
        var text = (cell ?? "").Trim().ToLowerInvariant();
        return text switch
        {
            "มี" or "yes" or "y" or "✓" or "true" or "1" or "have" => true,
            "ไม่มี" or "no" or "n" or "-" or "✗" or "false" or "0" => false,
            _ => null,
        };
    }
}
