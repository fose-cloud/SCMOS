namespace Scmos.Api.Rules;

/// <summary>
/// What a new trucking subcontractor hands over before it is approved (1 Oct 2026) — the department's list,
/// thirteen lines, in its order and its words.
///
/// <para>
/// Each line's files are the supplier's own documents (StoredDocument with the supplier id), filed with the
/// line's <see cref="Item.Kind"/>. Four lines are documents the Supplier Register already requires — the
/// affidavit, the transport licence and both insurances — and carry <see cref="SupplierCompliance"/>'s codes,
/// so a file uploaded while onboarding is the same file the compliance columns read.
/// </para>
/// </summary>
public static class VendorOnboarding
{
    public const string Pending = "pending";
    public const string Done = "done";
    public static readonly string[] Statuses = [Pending, Done];

    /// <param name="Note">The instruction the list writes beside it, in italics: where to sign and stamp.</param>
    /// <param name="Folder">The supplier tree it is filed in (<see cref="BlobPaths.SupplierFolders"/>).</param>
    /// <param name="Kind">The file's kind — a compliance code where the Supplier Register requires the document.</param>
    /// <param name="Link">A system the line names, opened from the list.</param>
    public sealed record Item(int No, string Code, string Document, string Note, string Folder, string Kind, string Link = "");

    public static readonly Item[] Items =
    [
        new(1, "iso-24", "ISO-FRM-TH-ISO-24: New Business Contact – Compliance Check", "", "Audit", "onboarding-iso-24"),
        new(2, "vmt-10", "ISO-FRM-TH-VMT-10: Trucking Service Provider Registration", "", "Contract", "onboarding-vmt-10"),
        new(3, "affidavit", "Company registration (Affidavit) not over 6 months", "", "Contract", "affidavit"),
        new(4, "vat-dbd", "Check VAT & DBD from Finance & Accounting staff", "", "Audit", "onboarding-vat-dbd"),
        new(5, "management-approval", "Email Approved from the Management of Overland Transport", "", "Audit", "onboarding-management-approval"),
        new(6, "transport-licence", "Transportation License (ใบประกอบการขนส่ง)", "", "License", "transport-licence"),
        new(7, "insurance-vehicle", "Valid Insurance Policy", "Motor Insurance", "Insurance", "insurance-vehicle"),
        new(8, "insurance-cargo", "Valid Insurance Policy", "Cargo Insurance", "Insurance", "insurance-cargo"),
        new(9, "id-prove", "Picture result of 1st compliance checking. (ID Prove)", "", "Audit", "onboarding-id-prove",
            "http://vm-idprvprd-001.hq.leschaco.org/IDproveWebclient/Einzelsuche"),
        new(10, "aeo-declaration", "Signed Security Declaration for Authorized Economic Operators (AEO)", "sign with company stamp", "Contract", "onboarding-aeo-declaration"),
        new(11, "code-of-conduct", "Signed Supplier Code of Conduct", "sign with company stamp on page 6", "Contract", "onboarding-code-of-conduct"),
        new(12, "requirement-profile", "Signed Leschaco Requirement Profile Truck Transports - Version 2020 - not classified", "sign with company stamp on page 18", "Contract", "onboarding-requirement-profile"),
        new(13, "requirement-profile-annex", "Signed Leschaco Requirement Profile Truck Transports - Version 2020 - Annex 1 classified", "sign with company stamp on page 7", "Contract", "onboarding-requirement-profile-annex"),
    ];

    public static Item? Find(string? code) =>
        Items.FirstOrDefault(item => item.Code.Equals((code ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
}
