namespace Scmos.Api.Rules;

/// <summary>
/// The paperwork a carrier's truck and driver must carry before they are on its register (30 Sep 2026).
///
/// <para>
/// Named by the department: a truck — head or tail — comes with its registration book, its motor insurance and
/// its cargo insurance; a driver comes with the driving licence. The carrier registers them on its own Capacity
/// screen and the upload is part of the registration, not a later step, so a truck without its papers is never
/// on the list a job is assigned from.
/// </para>
///
/// <para>
/// The states are <see cref="SupplierCompliance"/>'s — held, expiring inside the same sixty days, expired, or
/// held with no date written — so a truck's insurance and the company's read alike on every screen.
/// </para>
/// </summary>
public static class FleetDocuments
{
    public const string Head = "head";
    public const string Tail = "tail";
    public static readonly string[] Kinds = [Head, Tail];

    /// <param name="Code">The document's <c>Kind</c> when stored, and the form field its file arrives in.</param>
    /// <param name="Expires">Whether the document carries a date worth watching.</param>
    public sealed record Requirement(string Code, string English, string Thai, bool Expires);

    public static readonly Requirement[] Truck =
    [
        new("truck-registration-book", "Vehicle registration book", "เล่มทะเบียนรถ", false),
        new("truck-motor-insurance", "Motor insurance", "ประกันรถ", true),
        new("truck-cargo-insurance", "Cargo insurance", "ประกันสินค้า", true),
    ];

    public static readonly Requirement[] Driver =
    [
        new("driver-licence", "Driving licence", "ใบขับขี่", true),
    ];

    public static Requirement? TruckRequirement(string? code) =>
        Truck.FirstOrDefault(one => one.Code.Equals((code ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    public static Requirement? DriverRequirement(string? code) =>
        Driver.FirstOrDefault(one => one.Code.Equals((code ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A plate or licence number with its spacing and punctuation gone, upper case, Thai letters kept —
    /// "70-1234 กทม" and "70 1234กทม" are one truck. Used to refuse a second registration of the same thing.
    /// The register's own key (<see cref="SupplierRegister.Key"/>), not a second copy of it.
    /// </summary>
    public static string Key(string? value) => SupplierRegister.Key(value ?? "");

    /// <summary>A plate as it will be written on a job: trimmed, runs of spaces collapsed.</summary>
    public static string Tidy(string? value) =>
        string.Join(' ', (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
