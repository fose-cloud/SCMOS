namespace Scmos.Api.Data;

/// <summary>
/// A kind of lorry or box the team plans with, kept as a row so the list can
/// change without a deployment.
///
/// <para>
/// It started as three separate arrays in three files — one for the rate book,
/// one for the capacity board, one for the type column on a job — which is how
/// the same six-wheel lorry came to be spelled `6W` in one place and `1X6WH'`
/// in another. The two that describe <em>what the team dispatches</em> are this
/// table now. The rate book keeps its own, because a price list answers a
/// different question and is agreed with carriers rather than edited on a
/// Tuesday.
/// </para>
///
/// <para>
/// Rows are retired rather than deleted. A type that has been used is written
/// on real jobs and into their history, and removing it would leave those rows
/// pointing at nothing — so <see cref="Active"/> goes false, it leaves every
/// dropdown, and the jobs that already carry it still read correctly.
/// </para>
/// </summary>
public class VehicleTypeRow
{
    public int Id { get; set; }

    /// <summary>What is stored on a job and shown in the grid — `1X20'`, `1X6WH`.</summary>
    public string Code { get; set; } = "";

    /// <summary>How it reads in the dropdown, in the language the team keys in.</summary>
    public string Label { get; set; } = "";

    /// <summary>Lowest first. Ties fall back to the code, so the order is never arbitrary.</summary>
    public int Sort { get; set; }

    /// <summary>Offered in dropdowns. False means retired, not gone.</summary>
    public bool Active { get; set; } = true;

    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
