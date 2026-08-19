namespace Scmos.Api.Data;

/// <summary>
/// A file held in Blob Storage.
///
/// SQL keeps the metadata and the blob URL; the bytes never come near it. One
/// table for every file the system holds — a job's paperwork, a supplier's
/// insurance certificate, a CAR/PAR photo — because three tables would mean
/// three copies of "where does a file go", and every rule this codebase has
/// written down twice has drifted.
///
/// What a file belongs to is whichever of <see cref="JobKey"/>,
/// <see cref="SupplierId"/> and <see cref="CaseId"/> is set. The path in
/// <see cref="ObjectKey"/> says the same thing, deliberately: if the database and
/// the storage account ever disagree, the key alone still answers whose file this
/// is, what year it ran and what kind of document it is.
/// </summary>
public class StoredDocument
{
    public long Id { get; set; }

    /// <summary>job · supplier · report — which tree in <see cref="Rules.BlobPaths"/> this went to.</summary>
    public string Scope { get; set; } = "job";

    /// <summary>The job's key, when this is a job's paperwork.</summary>
    public string JobKey { get; set; } = "";

    public int? SupplierId { get; set; }

    /// <summary>The CAR/PAR case, when this file is evidence on one.</summary>
    public long? CaseId { get; set; }

    /// <summary>
    /// The driver, for a certificate or a photograph of them. A fourth owner
    /// rather than filing under their carrier: the paperwork belongs to the
    /// person, follows them if they move, and some drivers have no carrier.
    /// </summary>
    public int? DriverId { get; set; }

    /// <summary>
    /// The folder it went in: Booking · ECard · POD · Images · Invoice · CARPAR
    /// for a job; Audit · Insurance · License · Training · Contract for a
    /// supplier. Controlled — see <see cref="Rules.BlobPaths.JobFolders"/>.
    /// </summary>
    public string Folder { get; set; } = "Other";

    /// <summary>
    /// The caller's own word for what this is — "driver-statement", "cargo
    /// insurance". Kept alongside the folder because the folder is a controlled
    /// list and the kind is what a person actually wrote.
    /// </summary>
    public string Kind { get; set; } = "";

    /* ---- what the path was built from, so a key can be explained ---- */
    public string Year { get; set; } = "";
    public string Customer { get; set; } = "";
    public string JobRef { get; set; } = "";

    /// <summary>The name as it was uploaded, Thai and all. The blob name is cleaned; this is not.</summary>
    public string FileName { get; set; } = "";

    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }

    /// <summary>The full path within the container. Unique.</summary>
    public string ObjectKey { get; set; } = "";

    /// <summary>
    /// The blob's URL. The container is private, so this is a reference rather
    /// than a way in — reading goes through the API, which knows who is asking.
    /// </summary>
    public string BlobUrl { get; set; } = "";

    /// <summary>DD/MM/YYYY, for the documents compliance watches. Empty otherwise.</summary>
    public string ExpiryDate { get; set; } = "";

    public string Note { get; set; } = "";

    public string UploadedBy { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; }
}
