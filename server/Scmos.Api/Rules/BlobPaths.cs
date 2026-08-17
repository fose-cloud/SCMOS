using System.Text;

namespace Scmos.Api.Rules;

/// <summary>
/// Where a file goes in Blob Storage.
///
/// One root, three trees, and no caller composes a key by hand:
///
/// <code>
/// SCMOS/2026/LOTUS/ABS260800001/POD/20260817-141032-3f9c1a80-pod_signed.pdf
/// SCMOS/Supplier/DGT/Insurance/20260817-141105-8ab20c11-policy_2026.pdf
/// SCMOS/Report/2026/2026-07/20260801-090000-11ff30a2-july_plan.xlsx
/// </code>
///
/// The shape is the point. A flat container is unusable within a year: nobody can
/// find one job's paperwork, a lifecycle policy cannot be written against a
/// prefix, and access cannot be narrowed to a customer or a supplier later. The
/// path carries the year, the customer, the job and the kind of document, so all
/// four are answerable from the key alone — which matters when the database and
/// the storage account eventually disagree about something.
///
/// This lives in Rules rather than in the storage service because it is a
/// convention the whole system has to keep, not a detail of how bytes are
/// written. Every key is built here, so there is one copy of it.
/// </summary>
public static class BlobPaths
{
    public const string Root = "SCMOS";

    /// <summary>
    /// The folders a job's paperwork goes in.
    ///
    /// A controlled list, like the status vocabulary, and for the same reason:
    /// "POD", "pod" and "Proof of delivery" as three sibling folders is the mess
    /// this is meant to prevent. Anything unrecognised lands in Other rather
    /// than creating a folder nobody will look in again.
    /// </summary>
    public static readonly string[] JobFolders =
        ["Booking", "ECard", "POD", "Images", "Invoice", "CARPAR", "Other"];

    /// <summary>The folders a supplier's compliance file goes in.</summary>
    public static readonly string[] SupplierFolders =
        ["Audit", "Insurance", "License", "Training", "Contract", "Other"];

    /// <summary>
    /// The folder a written kind means.
    ///
    /// The spellings on the left are the ones already in the system — the
    /// evidence kinds on a CAR/PAR case, the document kinds on a supplier — so
    /// existing callers keep working and land in the right place.
    /// </summary>
    private static readonly Dictionary<string, string> JobAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["booking"] = "Booking", ["booking-confirmation"] = "Booking", ["do"] = "Booking",
        ["delivery-order"] = "Booking", ["bl"] = "Booking", ["bill-of-lading"] = "Booking",
        ["ecard"] = "ECard", ["e-card"] = "ECard", ["gate-pass"] = "ECard", ["gatepass"] = "ECard",
        ["pod"] = "POD", ["receipt"] = "POD", ["proof-of-delivery"] = "POD",
        ["image"] = "Images", ["images"] = "Images", ["photo"] = "Images", ["photos"] = "Images",
        ["invoice"] = "Invoice", ["inv"] = "Invoice", ["billing"] = "Invoice",
        ["carpar"] = "CARPAR", ["car"] = "CARPAR", ["par"] = "CARPAR", ["incident"] = "CARPAR",
        ["driver-statement"] = "CARPAR", ["supplier-report"] = "CARPAR",
        ["customer-information"] = "CARPAR",
    };

    private static readonly Dictionary<string, string> SupplierAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audit"] = "Audit", ["pre-audit"] = "Audit", ["evaluation"] = "Audit",
        ["insurance"] = "Insurance", ["cargo-insurance"] = "Insurance",
        ["licence"] = "License", ["license"] = "License", ["transport-licence"] = "License",
        ["iso"] = "License", ["registration"] = "License",
        ["training"] = "Training", ["safety-training"] = "Training", ["safety"] = "Training",
        ["contract"] = "Contract", ["agreement"] = "Contract", ["rate-card"] = "Contract",
    };

    public static string JobFolder(string wanted) => Folder(wanted, JobFolders, JobAliases);
    public static string SupplierFolder(string wanted) => Folder(wanted, SupplierFolders, SupplierAliases);

    private static string Folder(string wanted, string[] allowed, Dictionary<string, string> aliases)
    {
        var value = (wanted ?? "").Trim();
        if (value.Length == 0) return "Other";
        var exact = allowed.FirstOrDefault(name => name.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        return aliases.TryGetValue(value.Replace(' ', '-'), out var mapped) ? mapped : "Other";
    }

    /* ------------------------------------------------------------- keys */

    /// <summary>
    /// A job's paperwork: <c>SCMOS/{year}/{customer}/{jobRef}/{folder}/{file}</c>.
    ///
    /// The year comes from the job's own work date rather than the clock, so a
    /// July job filed in August is still under the year it ran — otherwise a
    /// year-end upload lands in the wrong tree and nobody finds it again.
    /// </summary>
    public static string ForJob(string year, string customer, string jobRef, string folder, string fileName) =>
        string.Join('/', Root, Segment(year, "0000"), Segment(customer, "UNKNOWN-CUSTOMER"),
            Segment(jobRef, "UNKNOWN-JOB"), JobFolder(folder), FileName(fileName));

    /// <summary>
    /// A supplier's compliance file: <c>SCMOS/Supplier/{code}/{folder}/{file}</c>.
    ///
    /// Keyed on the supplier code, not the name. The code is unique by
    /// construction and TATIYAPOL and TATIYAPON are deliberately separate
    /// companies until somebody says otherwise — filing both under a name that
    /// normalises the same way would quietly merge two firms' insurance.
    /// </summary>
    public static string ForSupplier(string code, string folder, string fileName) =>
        string.Join('/', Root, "Supplier", Segment(code, "UNKNOWN-SUPPLIER"),
            SupplierFolder(folder), FileName(fileName));

    /// <summary>
    /// A monthly report: <c>SCMOS/Report/{year}/{period}/{file}</c>.
    ///
    /// Not in the agreed tree, because reports are not a job's or a supplier's
    /// paperwork — but they still have to live somewhere, and the alternative is
    /// loose files at the container root, which is exactly what the structure is
    /// for.
    /// </summary>
    public static string ForReport(string year, string period, string fileName) =>
        string.Join('/', Root, "Report", Segment(year, "0000"),
            Segment(period, "unspecified"), FileName(fileName));

    /* -------------------------------------------------------- sanitising */

    /// <summary>
    /// One path segment.
    ///
    /// Letters and digits of any script survive, so Thai customer names stay
    /// distinct and readable; everything else — slashes above all — becomes a
    /// hyphen. A slash left in would silently invent a folder level and put the
    /// file somewhere the convention says nothing about.
    ///
    /// Names that differ only in punctuation collapse together: "T.O." and "TO"
    /// both become "TO". For suppliers that is correct and deliberate — it is the
    /// same reconciliation the register does. For customers it is a real, small
    /// risk, accepted because the alternative is percent-encoded folder names
    /// that nobody can read in the portal.
    /// </summary>
    public static string Segment(string value, string fallback)
    {
        var text = (value ?? "").Trim();
        var builder = new StringBuilder(text.Length);
        var lastWasDash = false;

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var cleaned = builder.ToString().Trim('-');
        if (cleaned.Length > 60) cleaned = cleaned[..60].TrimEnd('-');
        // "." and ".." are legal blob names and disastrous ones; the loop above
        // cannot produce them, but the guard says so out loud.
        return cleaned.Length == 0 || cleaned is "." or ".." ? fallback : cleaned;
    }

    /// <summary>
    /// The stored file name: sortable, unique, and recognisable.
    ///
    /// The timestamp puts a folder listing in the order things happened, and the
    /// short id means two people uploading POD.pdf in the same second do not
    /// overwrite each other — an upload must never destroy what is already
    /// there. The original name travels in the database row and the blob
    /// metadata, so nothing is lost by cleaning this one.
    /// </summary>
    public static string FileName(string original) =>
        $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}-{SafeName(original)}";

    /// <summary>
    /// Blob names accept far more than this, but a name that survives a URL, a
    /// download header and a Windows file system is worth more than a faithful
    /// Thai filename — the original is kept in the row and the blob metadata.
    ///
    /// The stem and the extension are cleaned separately, because a wholly Thai
    /// name cleans away to nothing and a naive trim takes the extension with it:
    /// "ใบส่งของ ลูกค้า.pdf" became "pdf", leaving a PDF that no operating system
    /// would open. The stem may be lost; the extension may not.
    /// </summary>
    public static string SafeName(string name)
    {
        var raw = Path.GetFileName(name ?? "");
        var stem = Clean(Path.GetFileNameWithoutExtension(raw));
        var extension = Clean(Path.GetExtension(raw).TrimStart('.'));

        if (stem.Length == 0) stem = "file";
        if (stem.Length > 80) stem = stem[..80].Trim('_', '-');

        return extension.Length == 0 ? stem : $"{stem}.{extension[..Math.Min(12, extension.Length)]}";

        static string Clean(string value) =>
            new string(value.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_')
                .ToArray()).Trim('_', '-');
    }

    /// <summary>
    /// The year a job's files belong to, from the plan's own DD/MM/YYYY date.
    /// Falls back to the current year when the job carries no usable date — a
    /// file with nowhere to go is worse than one filed under this year.
    /// </summary>
    public static string YearOf(string workDate)
    {
        var number = Formats.DateNumber(workDate);
        return number > 0 ? (number / 10000).ToString("D4") : DateTimeOffset.Now.Year.ToString("D4");
    }
}
