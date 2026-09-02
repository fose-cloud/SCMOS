using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record DocumentView(
    long Id, string Scope, string Folder, string Kind,
    string FileName, string ContentType, long SizeBytes,
    string ObjectKey, string BlobUrl, string ExpiryDate, string Note,
    string JobKey, int? SupplierId, long? CaseId,
    string Year, string Customer, string JobRef,
    string UploadedBy, DateTimeOffset UploadedAt, bool Expiring, bool Expired,
    /// <summary>Whether a screen may offer to open this rather than download it.</summary>
    bool CanShow);

public record DocumentResult(bool Ok, string Message, DocumentView? Document = null);

/// <param name="State">keep · review · overdue-review. Never "delete".</param>
public record RetentionItem(long Id, string FileName, string ObjectKey, string Scope, string Folder,
    DateTimeOffset UploadedAt, int AgeDays, string Tier, string State);

/// <summary>
/// Uploading and finding files.
///
/// Every key in the system is built here, through <see cref="BlobPaths"/>. No
/// endpoint composes one: a caller says what the file is attached to and what
/// kind it is, and the path follows from the register — the job's own work date
/// gives the year, its customer gives the folder, its job code gives the
/// reference. Handing the path to the caller would mean the structure is a
/// convention people remember rather than one the system keeps.
/// </summary>
public class DocumentService(ScmosDbContext db, IFileStore files)
{
    /// <summary>32 MB, matching the report upload limit the team already lives with.</summary>
    public const long MaxBytes = 32L * 1024 * 1024;

    /// <summary>How far ahead the compliance screen counts a document as expiring.</summary>
    public const int ExpiringWithinDays = 60;

    public bool StorageReady => files.Configured;

    /* --------------------------------------------------------- uploading */

    /// <summary>
    /// Files a job's paperwork.
    ///
    /// The job has to exist: a file attached to a job key nobody recognises is
    /// filed under a customer and a year that were guessed, which is worse than
    /// a refusal because it looks like it worked.
    /// </summary>
    public async Task<DocumentResult> AddToJobAsync(string jobKey, string folder, string kind,
        string note, IFormFile file, AppUser user, CancellationToken token)
    {
        var job = await db.OperationJobs.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Key == jobKey, token);
        if (job is null) return new DocumentResult(false, "ไม่พบงานนี้ในทะเบียน");

        var year = BlobPaths.YearOf(job.WorkDate);
        var reference = FirstFilled(job.JobCode, job.Container, job.Key);
        var key = BlobPaths.ForJob(year, job.Customer, reference, folder, file.FileName);

        return await StoreAsync(key, file, user, token, document =>
        {
            document.Scope = "job";
            document.JobKey = job.Key;
            document.Folder = BlobPaths.JobFolder(folder);
            document.Kind = kind.Trim();
            document.Note = note.Trim();
            document.Year = year;
            document.Customer = job.Customer;
            document.JobRef = reference;
        });
    }

    /// <summary>Files a supplier's compliance document, with the expiry that makes it worth watching.</summary>
    public async Task<DocumentResult> AddToSupplierAsync(int supplierId, string folder, string kind,
        string expiryDate, string note, IFormFile file, AppUser user, CancellationToken token)
    {
        var supplier = await db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == supplierId, token);
        if (supplier is null) return new DocumentResult(false, "ไม่พบผู้ขนส่งรายนี้");

        var expiry = expiryDate.Trim();
        if (expiry.Length > 0 && Formats.DateNumber(expiry) == 0)
            return new DocumentResult(false, "วันหมดอายุต้องเป็นรูปแบบ DD/MM/YYYY");

        var key = BlobPaths.ForSupplier(supplier.Code, folder, file.FileName);

        return await StoreAsync(key, file, user, token, document =>
        {
            document.Scope = "supplier";
            document.SupplierId = supplier.Id;
            document.Folder = BlobPaths.SupplierFolder(folder);
            document.Kind = kind.Trim();
            document.ExpiryDate = expiry;
            document.Note = note.Trim();
            document.Customer = supplier.Code;
        });
    }

    /// <summary>
    /// A driver's own paperwork — a certificate, or a photograph of them.
    ///
    /// Filed under the driver rather than whichever carrier employed them the
    /// day it was scanned: the certificate belongs to the person, follows them
    /// if they move, and some drivers have no carrier at all.
    /// </summary>
    public async Task<DocumentResult> AddToDriverAsync(int driverId, string folder, string kind,
        string expiryDate, string note, IFormFile file, AppUser user, CancellationToken token)
    {
        var driver = await db.Drivers.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == driverId, token);
        if (driver is null) return new DocumentResult(false, "ไม่พบคนขับรายนี้");

        var expiry = expiryDate.Trim();
        if (expiry.Length > 0 && Formats.DateNumber(expiry) == 0)
            return new DocumentResult(false, "วันหมดอายุต้องเป็นรูปแบบ DD/MM/YYYY");

        // Keyed on the licence number, falling back to the row id, so a driver
        // with no number recorded still files somewhere findable rather than
        // under a folder called UNKNOWN with everybody else.
        var key = BlobPaths.ForDriver(
            driver.DriverIdNo.Trim().Length > 0 ? driver.DriverIdNo : $"ID-{driver.Id}",
            folder, file.FileName);

        return await StoreAsync(key, file, user, token, document =>
        {
            document.Scope = "driver";
            document.DriverId = driver.Id;
            document.Folder = folder.Trim().Length > 0 ? folder.Trim() : "Training";
            document.Kind = kind.Trim();
            document.ExpiryDate = expiry;
            document.Note = note.Trim();
            document.Customer = driver.Name;
        });
    }

    /// <summary>
    /// Attaches evidence to a CAR/PAR case.
    ///
    /// It lands in the job's own CARPAR folder when the case came from a job, so
    /// a shipment's whole story — booking, POD, photos, the case that followed —
    /// is in one place. A case raised on its own has no job to file under and
    /// goes under the year instead.
    /// </summary>
    public async Task<DocumentResult> AddToCaseAsync(long caseId, string kind, string note,
        IFormFile file, AppUser user, CancellationToken token)
    {
        var record = await db.IncidentCases.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == caseId, token);
        if (record is null) return new DocumentResult(false, "ไม่พบเคสนี้");

        var job = record.JobKey.Length == 0 ? null : await db.OperationJobs.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Key == record.JobKey, token);

        var year = job is null
            ? record.RaisedAt.Year.ToString("D4")
            : BlobPaths.YearOf(job.WorkDate);
        var customer = job?.Customer ?? "CARPAR";
        var reference = job is null ? record.Reference : FirstFilled(job.JobCode, job.Container, job.Key);
        var key = BlobPaths.ForJob(year, customer, reference, "CARPAR", file.FileName);

        return await StoreAsync(key, file, user, token, document =>
        {
            document.Scope = "job";
            document.CaseId = record.Id;
            document.JobKey = record.JobKey;
            document.Folder = "CARPAR";
            document.Kind = kind.Trim().Length > 0 ? kind.Trim() : "photo";
            document.Note = note.Trim();
            document.Year = year;
            document.Customer = customer;
            document.JobRef = reference;
        });
    }

    /// <summary>
    /// A photograph or a document attached while an issue is being logged.
    ///
    /// Filed in the job's own tree when the issue names a job, under Images,
    /// because that is where somebody looking through a job's paperwork expects
    /// to find a picture of what went wrong. An issue whose reference never
    /// matched a job goes under the year and ISSUE instead — that reference
    /// still describes something real, and refusing the file because the
    /// register has no row for it would lose the only evidence there is.
    /// </summary>
    public async Task<DocumentResult> AddToIssueAsync(long issueId, string kind, string note,
        IFormFile file, AppUser user, CancellationToken token)
    {
        var record = await db.OperationalIssues.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == issueId, token);
        if (record is null) return new DocumentResult(false, "ไม่พบรายการปัญหานี้");

        var job = record.JobKey.Length == 0 ? null : await db.OperationJobs.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Key == record.JobKey, token);

        var year = job is null
            ? (record.CreatedAt == default ? DateTimeOffset.Now : record.CreatedAt).Year.ToString("D4")
            : BlobPaths.YearOf(job.WorkDate);
        var customer = job?.Customer ?? "ISSUE";
        var reference = job is null
            ? FirstFilled(record.Code, record.JobRef, record.Id.ToString())
            : FirstFilled(job.JobCode, job.Container, job.Key);
        var key = BlobPaths.ForJob(year, customer, reference, "Images", file.FileName);

        return await StoreAsync(key, file, user, token, document =>
        {
            document.Scope = "job";
            document.IssueId = record.Id;
            document.JobKey = record.JobKey;
            document.Folder = "Images";
            document.Kind = kind.Trim().Length > 0 ? kind.Trim() : "photo";
            document.Note = note.Trim();
            document.Year = year;
            document.Customer = customer;
            document.JobRef = reference;
        });
    }

    /// <summary>The one place bytes are written and a row is recorded.</summary>
    private async Task<DocumentResult> StoreAsync(string objectKey, IFormFile file, AppUser user,
        CancellationToken token, Action<StoredDocument> describe)
    {
        if (!files.Configured)
            return new DocumentResult(false,
                "ยังไม่ได้ตั้งค่าที่เก็บไฟล์ — ตั้ง Storage:ServiceUri และให้สิทธิ์ Storage Blob Data Contributor กับ managed identity");
        if (file.Length == 0) return new DocumentResult(false, "ไฟล์ว่าง");
        if (file.Length > MaxBytes) return new DocumentResult(false, "ไฟล์ใหญ่เกิน 32 MB");

        var document = new StoredDocument
        {
            ObjectKey = objectKey,
            FileName = file.FileName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
            UploadedBy = user.Signature,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        describe(document);

        await using (var stream = file.OpenReadStream())
        {
            document.BlobUrl = await files.PutAsync(objectKey, stream, document.ContentType,
                new Dictionary<string, string>
                {
                    // Blob metadata headers are ASCII-only, so the original name
                    // is kept in the database row and only a cleaned copy here.
                    ["originalName"] = BlobPaths.SafeName(file.FileName),
                    ["scope"] = document.Scope,
                    ["folder"] = document.Folder,
                    ["uploadedBy"] = BlobPaths.SafeName(user.Signature),
                }, token);
        }

        db.Documents.Add(document);
        await db.SaveChangesAsync(token);

        return new DocumentResult(true, $"อัปโหลด {file.FileName} แล้ว", Describe(document));
    }

    /* ----------------------------------------------------------- reading */

    public async Task<IReadOnlyList<DocumentView>> ListAsync(string? jobKey, int? supplierId,
        long? caseId, long? issueId, string? folder, CancellationToken token)
    {
        var query = db.Documents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(jobKey)) query = query.Where(d => d.JobKey == jobKey);
        if (supplierId is not null) query = query.Where(d => d.SupplierId == supplierId);
        if (caseId is not null) query = query.Where(d => d.CaseId == caseId);
        if (issueId is not null) query = query.Where(d => d.IssueId == issueId);
        if (!string.IsNullOrWhiteSpace(folder) && folder != "All")
            query = query.Where(d => d.Folder == folder);

        var rows = await query.OrderByDescending(d => d.Id).Take(500).ToListAsync(token);
        return rows.Select(Describe).ToList();
    }

    public async Task<StoredDocument?> FindAsync(long id, CancellationToken token) =>
        await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, token);

    public Task<Stream?> OpenAsync(StoredDocument document, CancellationToken token) =>
        files.OpenAsync(document.ObjectKey, token);

    /// <summary>How many of a supplier's documents expire within the watch window.</summary>
    public static bool IsExpiring(string expiryDate)
    {
        var due = Formats.DateNumber(expiryDate);
        if (due == 0) return false;
        var soon = Formats.DateNumber(DateTimeOffset.Now.AddDays(ExpiringWithinDays).ToString("dd/MM/yyyy"));
        var today = Formats.DateNumber(DateTimeOffset.Now.ToString("dd/MM/yyyy"));
        return due >= today && due <= soon;
    }

    public static bool IsExpired(string expiryDate)
    {
        var due = Formats.DateNumber(expiryDate);
        return due > 0 && due < Formats.DateNumber(DateTimeOffset.Now.ToString("dd/MM/yyyy"));
    }

    /// <summary>
    /// Documents at or near the end of their ten-year retention.
    ///
    /// A list for a person to work through, not a queue for a job to drain.
    /// Nothing in this service deletes a document, and nothing calls anything
    /// that would — see <see cref="Retention"/> for why that is a refusal rather
    /// than an omission.
    /// </summary>
    public async Task<IReadOnlyList<RetentionItem>> RetentionReviewAsync(bool includeAll, CancellationToken token)
    {
        var rows = await db.Documents.AsNoTracking()
            .OrderBy(document => document.UploadedAt).Take(1000).ToListAsync(token);

        return rows.Select(document =>
            {
                var age = Retention.AgeDays(document.UploadedAt);
                return new RetentionItem(document.Id, document.FileName, document.ObjectKey,
                    document.Scope, document.Folder, document.UploadedAt, age,
                    Retention.TierFor(age), Retention.StateFor(age));
            })
            .Where(item => includeAll || item.State != "keep")
            .ToList();
    }

    public static DocumentView Describe(StoredDocument d) => new(
        d.Id, d.Scope, d.Folder, d.Kind, d.FileName, d.ContentType, d.SizeBytes,
        d.ObjectKey, d.BlobUrl, d.ExpiryDate, d.Note,
        d.JobKey, d.SupplierId, d.CaseId, d.Year, d.Customer, d.JobRef,
        d.UploadedBy, d.UploadedAt, IsExpiring(d.ExpiryDate), IsExpired(d.ExpiryDate),
        // Decided here, from the same rule the content route serves by, so the
        // screen never offers to open something the API would refuse to show.
        Rules.InlineViewing.CanShow(d.FileName));

    private static string FirstFilled(params string[] values) =>
        values.FirstOrDefault(value => value.Trim().Length > 0)?.Trim() ?? "";
}
