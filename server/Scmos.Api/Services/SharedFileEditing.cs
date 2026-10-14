using System.Text;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public partial class SharedFilesService
{
    // Saving a new version is open to everybody with the upload right (the user's choice, 14 Oct 2026): every save
    // keeps the bytes it replaces, so nothing an editor does is lost. Renaming and the trash stay with the uploader
    // or a supervisor (CanChange).
    public static string? EditorKind(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".xlsx" => "excel", ".pdf" => "pdf",
        ".jpg" or ".jpeg" or ".png" or ".webp" => "image", _ => null,
    };

    public async Task<SharedResult> EditorAsync(AppUser user, long id, CancellationToken token)
    {
        var (file, stream, refusal) = await OpenAsync(user, id, token);
        if (refusal is not null) return refusal;
        await using var content = stream!;
        var kind = EditorKind(file!.Name);
        if (kind is null) return new(false, 415, "ไฟล์ชนิดนี้ใช้ดาวน์โหลด/เปิดดูเท่านั้น — editor รองรับ XLSX, PDF, JPG, PNG และ WebP");
        SharedWorkbook.View? workbook = null;
        if (kind == "excel")
        {
            try { workbook = SharedWorkbook.Read(await ReadLimitedAsync(content, token)); }
            catch (Exception e) when (e is InvalidDataException or XmlException or FormatException or ArgumentException)
            { return new(false, 422, "เปิด XLSX ไม่ได้อย่างปลอดภัย — กรุณาดาวน์โหลดไปใช้ Excel"); }
        }
        return new(true, 200, "", new
        {
            id, name = file.Name, kind, revision = file.Revision, version = Convert.ToBase64String(file.Version),
            canEdit = CanWrite(user), workbook,
            maxBytes = SharedFiles.MaxBytes,
            history = (await db.SharedFileRevisions.AsNoTracking().Where(r => r.FileId == id)
                .OrderByDescending(r => r.Revision).ToListAsync(token))
                .Select(r => new { revision = r.Revision, name = r.Name, savedBy = r.SavedBy, savedAt = r.SavedAt }),
        });
    }

    public async Task<SharedResult> EditCellsAsync(AppUser user, long id, string? version,
        IReadOnlyList<SharedWorkbook.CellEdit>? edits, CancellationToken token)
    {
        if (!CanWrite(user)) return new(false, 403, "บัญชีนี้ไม่มีสิทธิ์แก้ไขไฟล์");
        var (file, stream, refusal) = await OpenAsync(user, id, token);
        if (refusal is not null) return refusal;
        await using var content = stream!;
        if (Path.GetExtension(file!.Name).ToLowerInvariant() != ".xlsx") return new(false, 415, "ไฟล์นี้ไม่ใช่ XLSX");
        if (!SameVersion(file, version)) return Conflict();
        if (edits is null) return new(false, 400, "ต้องระบุเซลล์ที่แก้ไข");
        byte[] saved;
        try { saved = SharedWorkbook.Edit(await ReadLimitedAsync(content, token), edits); }
        catch (Exception e) when (e is InvalidDataException or XmlException or FormatException or ArgumentException)
        { return new(false, 422, e is InvalidDataException ? e.Message : "แก้ไข XLSX ไม่ได้อย่างปลอดภัย — กรุณาใช้ Excel"); }
        return await SaveEditedAsync(user, file, version, saved, token);
    }

    public async Task<SharedResult> EditContentAsync(AppUser user, long id, string? version, IFormFile? upload, CancellationToken token)
    {
        if (!CanWrite(user)) return new(false, 403, "บัญชีนี้ไม่มีสิทธิ์แก้ไขไฟล์");
        var (file, stream, refusal) = await OpenAsync(user, id, token);
        if (refusal is not null) return refusal;
        await using var original = stream!;
        if (!SameVersion(file!, version)) return Conflict();
        if (upload is null || upload.Length == 0) return new(false, 400, "ต้องแนบไฟล์หลังแก้ไข");
        if (upload.Length > SharedFiles.MaxBytes) return new(false, 413, "ไฟล์ใหญ่เกิน 32 MB");
        await using var input = upload.OpenReadStream();
        var bytes = await ReadLimitedAsync(input, token);
        var extension = Path.GetExtension(file!.Name).ToLowerInvariant();
        var valid = extension switch
        {
            ".pdf" => bytes.AsSpan().StartsWith("%PDF-"u8),
            ".png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }),
            ".webp" => bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false,
        };
        if (!valid) return new(false, 415, "รับเฉพาะ PDF/รูปภาพชนิดเดิมที่แก้ไขใน editor — XLSX ต้องส่งเป็นการแก้เซลล์");
        if (extension == ".pdf")
        {
            // Additional defense for the common uncompressed signature/encryption dictionaries.
            // The client also checks parsed PDF objects, including compressed objects, before enabling edits.
            var oldText = Encoding.Latin1.GetString(await ReadLimitedAsync(original, token));
            if (oldText.Contains("/ByteRange", StringComparison.Ordinal) || oldText.Contains("/Encrypt", StringComparison.Ordinal))
                return new(false, 422, "PDF ลงลายเซ็นดิจิทัลหรือเข้ารหัส — อ่านอย่างเดียว");
        }
        return await SaveEditedAsync(user, file, version, bytes, token);
    }

    private static bool SameVersion(SharedFile file, string? expected) =>
        !string.IsNullOrEmpty(expected) && Convert.ToBase64String(file.Version) == expected;
    private static SharedResult Conflict() => new(false, 409, "ไฟล์ถูกแก้ไข เปลี่ยนชื่อ หรือลบโดยผู้ใช้อื่นแล้ว — ปิดแล้วเปิดฉบับล่าสุดใหม่ (ข้อมูลที่กรอกยังไม่ถูกบันทึก)");

    private async Task<SharedResult> SaveEditedAsync(AppUser user, SharedFile before, string? version, byte[] bytes, CancellationToken token)
    {
        var key = BlobPaths.ForShared(before.FolderId, before.Name);
        using var stream = new MemoryStream(bytes, false);
        await files.PutAsync(key, stream, before.ContentType, new Dictionary<string, string>
        { ["scope"] = "shared", ["originalName"] = BlobPaths.SafeName(before.Name), ["uploadedBy"] = BlobPaths.SafeName(user.Email) }, token);
        var revision = 0;
        try
        {
            await EvaluationWrites.InOneAsync(db, async () =>
            {
                var row = await db.SharedFiles.FirstOrDefaultAsync(r => r.Id == before.Id, token);
                if (row is null || row.DeletedAt is not null || !SameVersion(row, version)
                    || !CanWrite(user) || (await OpenFolderAsync(row.FolderId, token)).Problem is not null)
                    throw new DbUpdateConcurrencyException();
                db.SharedFileRevisions.Add(new SharedFileRevision
                {
                    FileId = row.Id, Revision = row.Revision, Name = row.Name, ObjectKey = row.ObjectKey,
                    ContentType = row.ContentType, SizeBytes = row.SizeBytes,
                    SavedBy = row.EditedAt is null ? row.UploadedBy : row.EditedBy,
                    SavedAt = row.EditedAt ?? row.UploadedAt,
                });
                row.ObjectKey = key; row.SizeBytes = bytes.LongLength; row.EditedBy = Who(user); row.EditedAt = DateTimeOffset.UtcNow;
                row.Revision++; revision = row.Revision;
                audit.Stage(user, AuditActions.Update, FileEntity, row.Id.ToString(), row.Name, "revision",
                    before.Revision.ToString(), row.Revision.ToString(), "Basic online editor; previous bytes retained");
                await db.SaveChangesAsync(token); // rowversion CAS + immutable history + audit commit together
            }, token);
        }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        catch (DbUpdateException e) when (EvaluationWrites.Duplicate(e)) { return Conflict(); }
        return new(true, 200, "บันทึกฉบับใหม่และเก็บฉบับเดิมแล้ว", new { revision, message = "บันทึกฉบับใหม่และเก็บฉบับเดิมแล้ว" });
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream input, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > SharedFiles.MaxBytes) throw new InvalidDataException("ไฟล์ใหญ่เกิน 32 MB");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return output.ToArray();
    }
}
