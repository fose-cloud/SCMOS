namespace Scmos.Api.Rules;

/// <summary>
/// Which Job Rotation customer a rate lane is priced for (1 Oct 2026) — so a Billing Case's Rate price is the
/// price agreed for that customer, not another customer's on the same road.
///
/// <para>
/// A carrier's form does not say reliably which column is the customer: TNB write it under Customer, SANGJA
/// under From. So the carrier's text is never rewritten. A lane carries its own link, picked in Rate
/// Management from the Job Rotation list (the same list My job's customer column offers); until somebody picks,
/// a lane whose Customer or From text is exactly a rotation customer counts as that customer's.
/// </para>
/// </summary>
public static class LaneCustomer
{
    /// <summary>A link somebody picked in Rate Management.</summary>
    public const string Picked = "picked";

    /// <summary>No link picked; the lane's Customer or From text is exactly a rotation customer.</summary>
    public const string Matched = "matched";

    /// <summary>Picked as any customer's: the lane prices whoever's job fits it.</summary>
    public const string General = "general";

    /// <summary>
    /// How Job Rotation and My job compare a customer name: trimmed, upper case (<c>rotationCustomers.ts</c>).
    /// </summary>
    public static string Key(string? name) => (name ?? "").Trim().ToUpperInvariant();

    /// <summary>
    /// The lane's customer and how it was found. <paramref name="picked"/> is the stored link: null when nobody
    /// has picked, empty when picked as any customer's, else the rotation spelling. <paramref name="rotation"/>
    /// maps <see cref="Key"/> to the rotation's own spelling.
    /// </summary>
    public static (string Customer, string Link) Of(string? picked, string customerText, string fromText,
        IReadOnlyDictionary<string, string> rotation)
    {
        if (picked is not null) return picked.Length == 0 ? ("", General) : (picked, Picked);
        if (rotation.TryGetValue(Key(customerText), out var byCustomer)) return (byCustomer, Matched);
        if (rotation.TryGetValue(Key(fromText), out var byFrom)) return (byFrom, Matched);
        return ("", "");
    }

    /// <summary>Whether a lane linked to <paramref name="laneCustomer"/> may price a job for <paramref name="jobCustomer"/>.</summary>
    public static bool Serves(string laneCustomer, string jobCustomer) =>
        laneCustomer.Length == 0 || Key(laneCustomer) == Key(jobCustomer);
}
