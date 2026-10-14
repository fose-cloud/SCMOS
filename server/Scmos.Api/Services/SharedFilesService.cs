using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>What a shared-file action came to: the status to answer with, the words to show, and anything to return.</summary>
public sealed record SharedResult(bool Ok, int Status, string Message, object? Body = null);

/// <summary>
/// The shared file space (14 Oct 2026) — see <see cref="SharedFiles"/> for the rules shared with the UI.
///
/// <para>
/// Who may do what: every staff account sees everything and no carrier sees anything; the upload right
/// (<see cref="Capability.UploadDocuments"/>) makes folders and files; whoever made an item, or a supervisor, renames it
/// or moves it to the trash; only a supervisor restores. Nothing is ever deleted — from the database or from storage —
/// so a mistaken delete is a restore, never a recovery.
/// </para>
/// </summary>
public partial class SharedFilesService(ScmosDbContext db, IFileStore files, AuditService audit)
{
    public const string FolderEntity = "shared-folder";
    public const string FileEntity = "shared-file";

    public bool StorageReady => files.Configured;

    /// <summary>Every account of the department; a carrier's never — it holds the upload right for its own papers only.</summary>
    public static bool CanSee(AppUser user) =>
        user.Recognised && !string.IsNullOrWhiteSpace(user.UserId) && Roles.Find(user.Role) is { } role
        && role.Name != Roles.Subcontractor;

    public static bool CanWrite(AppUser user) => CanSee(user) && user.Can(Capability.UploadDocuments);

    public static bool CanRestore(AppUser user) => CanSee(user) && Roles.IsSupervisor(user.Role);

    /// <summary>Whoever made the item, or a supervisor — and either way only with the upload right.</summary>
    public static bool CanChange(AppUser user, string ownerId) =>
        CanWrite(user) && ((ownerId.Length > 0 && ownerId == user.UserId) || Roles.IsSupervisor(user.Role));

    /// <summary>The name the list shows for a person: "Given Surname", whichever way round the directory sends it.</summary>
    private static string Who(AppUser user) =>
        Endpoints.MeEndpoints.GivenFirst(user.DisplayName) is { Length: > 0 } name ? name : user.Email;

    /// <summary>
    /// A live folder and the path down to it, or why not. A folder whose own row or any folder above it is in the trash
    /// is not there, as far as anybody browsing is concerned.
    /// </summary>
    private async Task<(SharedFolder? Folder, List<SharedFolder> Path, string? Problem)> OpenFolderAsync(long? id, CancellationToken token)
    {
        if (id is null) return (null, [], null);
        var path = new List<SharedFolder>();
        long? cursor = id;
        while (cursor is long at)
        {
            if (path.Count >= SharedFiles.MaxDepth) return (null, [], "ไม่พบโฟลเดอร์นี้");
            var row = await db.SharedFolders.AsNoTracking().FirstOrDefaultAsync(folder => folder.Id == at, token);
            if (row is null || row.DeletedAt is not null) return (null, [], "ไม่พบโฟลเดอร์นี้");
            path.Insert(0, row);
            cursor = row.ParentId;
        }
        return (path[^1], path, null);
    }

    /* ------------------------------------------------------------------ reading */

    public async Task<SharedResult> ListAsync(AppUser user, long? folderId, CancellationToken token)
    {
        if (!CanSee(user)) return new(false, StatusCodes.Status403Forbidden, "เมนูนี้สำหรับบัญชีของพนักงาน");
        var (folder, path, problem) = await OpenFolderAsync(folderId, token);
        if (problem is not null) return new(false, StatusCodes.Status404NotFound, problem);

        var folders = await db.SharedFolders.AsNoTracking()
            .Where(row => row.ParentId == folderId && row.DeletedAt == null).OrderBy(row => row.Name).ToListAsync(token);
        var fileRows = await db.SharedFiles.AsNoTracking()
            .Where(row => row.FolderId == folderId && row.DeletedAt == null).OrderBy(row => row.Name).ToListAsync(token);

        var ids = folders.Select(row => (long?)row.Id).ToList();
        var subfolders = await db.SharedFolders.AsNoTracking()
            .Where(row => ids.Contains(row.ParentId) && row.DeletedAt == null)
            .GroupBy(row => row.ParentId).Select(group => new { group.Key, Count = group.Count() }).ToListAsync(token);
        var contained = await db.SharedFiles.AsNoTracking()
            .Where(row => ids.Contains(row.FolderId) && row.DeletedAt == null)
            .GroupBy(row => row.FolderId).Select(group => new { group.Key, Count = group.Count() }).ToListAsync(token);
        int Items(long id) =>
            (subfolders.FirstOrDefault(one => one.Key == id)?.Count ?? 0) + (contained.FirstOrDefault(one => one.Key == id)?.Count ?? 0);

        return new(true, StatusCodes.Status200OK, "", new
        {
            folder = folder is null ? null : new { id = folder.Id, name = folder.Name },
            path = path.Select(row => new { id = row.Id, name = row.Name }),
            folders = folders.Select(row => new
            {
                id = row.Id, name = row.Name, createdBy = row.CreatedBy, createdAt = row.CreatedAt, items = Items(row.Id),
                canChange = CanChange(user, row.CreatedById),
            }),
            files = fileRows.Select(row => new
            {
                id = row.Id, name = row.Name, sizeBytes = row.SizeBytes, uploadedBy = row.UploadedBy, uploadedAt = row.UploadedAt,
                canChange = CanChange(user, row.UploadedById), viewable = InlineViewing.TypeFor(row.Name) is not null,
                online = EditorKind(row.Name) is not null, revision = row.Revision,
                editedBy = row.EditedBy, editedAt = row.EditedAt,
            }),
            canUpload = CanWrite(user),
            canCreateFolder = CanWrite(user) && path.Count < SharedFiles.MaxDepth,
            canRestore = CanRestore(user),
            storageReady = files.Configured,
            limits = new { maxBytes = SharedFiles.MaxBytes, maxFolderName = SharedFiles.MaxFolderName,
                maxFileName = SharedFiles.MaxFileName, extensions = SharedFiles.Extensions },
        });
    }

    /// <summary>A live file and its bytes, for download. A file in the trash, or under a folder that is, opens for nobody.</summary>
    public async Task<(SharedFile? File, Stream? Content, SharedResult? Refusal)> OpenAsync(AppUser user, long id, CancellationToken token, int? revision = null)
    {
        if (!CanSee(user)) return (null, null, new(false, StatusCodes.Status403Forbidden, "เมนูนี้สำหรับบัญชีของพนักงาน"));
        var row = await db.SharedFiles.AsNoTracking().FirstOrDefaultAsync(file => file.Id == id, token);
        if (row is null || row.DeletedAt is not null) return (null, null, new(false, StatusCodes.Status404NotFound, "ไม่พบไฟล์นี้"));
        if ((await OpenFolderAsync(row.FolderId, token)).Problem is not null)
            return (null, null, new(false, StatusCodes.Status404NotFound, "ไม่พบไฟล์นี้"));
        if (!files.Configured) return (null, null, new(false, StatusCodes.Status503ServiceUnavailable, "ยังไม่ได้ตั้งค่าที่เก็บไฟล์"));
        if (revision is not null && revision != row.Revision)
        {
            var previous = await db.SharedFileRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.FileId == id && r.Revision == revision, token);
            if (previous is null) return (null, null, new(false, StatusCodes.Status404NotFound, "ไม่พบเวอร์ชันนี้"));
            row.Name = previous.Name; row.ObjectKey = previous.ObjectKey; row.ContentType = previous.ContentType;
            row.SizeBytes = previous.SizeBytes; row.Revision = previous.Revision;
        }
        var content = await files.OpenAsync(row.ObjectKey, token);
        return content is null
            ? (null, null, new(false, StatusCodes.Status404NotFound, $"ไฟล์หายจากที่เก็บ: {row.ObjectKey}"))
            : (row, content, null);
    }

    /// <summary>What is in the trash, newest first. Everybody may look — it is where a missing file is found.</summary>
    public async Task<SharedResult> TrashAsync(AppUser user, CancellationToken token)
    {
        if (!CanSee(user)) return new(false, StatusCodes.Status403Forbidden, "เมนูนี้สำหรับบัญชีของพนักงาน");
        var folders = await db.SharedFolders.AsNoTracking().Where(row => row.DeletedAt != null)
            .OrderByDescending(row => row.DeletedAt).Take(200).ToListAsync(token);
        var fileRows = await db.SharedFiles.AsNoTracking().Where(row => row.DeletedAt != null)
            .OrderByDescending(row => row.DeletedAt).Take(200).ToListAsync(token);
        var parentIds = folders.Select(row => row.ParentId).Concat(fileRows.Select(row => row.FolderId))
            .OfType<long>().Distinct().ToList();
        var parents = await db.SharedFolders.AsNoTracking().Where(row => parentIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.Name, token);
        string Where(long? parent) => parent is long at ? parents.GetValueOrDefault(at, "") : "";

        var items = folders.Select(row => new TrashItem("folder", row.Id, row.Name, Where(row.ParentId), null, row.DeletedAt!.Value, row.DeletedBy))
            .Concat(fileRows.Select(row => new TrashItem("file", row.Id, row.Name, Where(row.FolderId), row.SizeBytes, row.DeletedAt!.Value, row.DeletedBy)))
            .OrderByDescending(item => item.DeletedAt).Take(200).ToList();
        return new(true, StatusCodes.Status200OK, "", new { items, canRestore = CanRestore(user) });
    }

    private sealed record TrashItem(string Kind, long Id, string Name, string Location, long? SizeBytes, DateTimeOffset DeletedAt, string DeletedBy);

    /* ------------------------------------------------------------------ folders */

    public async Task<SharedResult> CreateFolderAsync(AppUser user, long? parentId, string? name, CancellationToken token)
    {
        if (!CanWrite(user)) return new(false, StatusCodes.Status403Forbidden, "บัญชีนี้ไม่มีสิทธิ์สร้างโฟลเดอร์");
        if (SharedFiles.NameProblem(name, SharedFiles.MaxFolderName) is { } problem) return new(false, StatusCodes.Status400BadRequest, problem);
        var (_, path, missing) = await OpenFolderAsync(parentId, token);
        if (missing is not null) return new(false, StatusCodes.Status404NotFound, missing);
        if (path.Count >= SharedFiles.MaxDepth)
            return new(false, StatusCodes.Status400BadRequest, $"สร้างโฟลเดอร์ซ้อนกันได้ไม่เกิน {SharedFiles.MaxDepth} ชั้น");

        var clean = name!.Trim();
        if (await db.SharedFolders.AnyAsync(row => row.ParentId == parentId && row.DeletedAt == null && row.Name == clean, token))
            return new(false, StatusCodes.Status409Conflict, "มีโฟลเดอร์ชื่อนี้อยู่แล้ว");

        SharedFolder folder = null!;
        try
        {
            // Reuse the existing retry-safe transaction helper: metadata and its audit must land together.
            await EvaluationWrites.InOneAsync(db, async () =>
            {
                folder = new SharedFolder
                {
                    ParentId = parentId, Name = clean, CreatedById = user.UserId, CreatedBy = Who(user), CreatedAt = DateTimeOffset.UtcNow,
                };
                db.SharedFolders.Add(folder);
                await db.SaveChangesAsync(token);
                audit.Stage(user, AuditActions.Create, FolderEntity, folder.Id.ToString(), clean, "name", "", clean, "");
                await db.SaveChangesAsync(token);
            }, token);
        }
        catch (DbUpdateException error) when (EvaluationWrites.Duplicate(error))
        { return new(false, StatusCodes.Status409Conflict, "มีโฟลเดอร์ชื่อนี้อยู่แล้ว"); }
        var created = $"สร้างโฟลเดอร์ {clean} แล้ว";
        return new(true, StatusCodes.Status200OK, created, new { id = folder.Id, message = created });
    }

    public async Task<SharedResult> RenameFolderAsync(AppUser user, long id, string? name, CancellationToken token)
    {
        if (!CanWrite(user)) return new(false, StatusCodes.Status403Forbidden, "บัญชีนี้ไม่มีสิทธิ์แก้ไขโฟลเดอร์");
        var folder = await db.SharedFolders.FirstOrDefaultAsync(row => row.Id == id, token);
        if (folder is null || (await OpenFolderAsync(id, token)).Problem is not null)
            return new(false, StatusCodes.Status404NotFound, "ไม่พบโฟลเดอร์นี้");
        if (!CanChange(user, folder.CreatedById))
            return new(false, StatusCodes.Status403Forbidden, "เปลี่ยนชื่อได้เฉพาะผู้สร้างโฟลเดอร์ หรือ Supervisor ขึ้นไป");
        if (SharedFiles.NameProblem(name, SharedFiles.MaxFolderName) is { } problem) return new(false, StatusCodes.Status400BadRequest, problem);

        var clean = name!.Trim();
        if (clean == folder.Name) return new(true, StatusCodes.Status200OK, "ชื่อเดิม ไม่มีอะไรเปลี่ยน");
        if (await db.SharedFolders.AnyAsync(row => row.Id != id && row.ParentId == folder.ParentId && row.DeletedAt == null
                && row.Name == clean, token))
            return new(false, StatusCodes.Status409Conflict, "มีโฟลเดอร์ชื่อนี้อยู่แล้ว");

        var before = folder.Name;
        folder.Name = clean;
        audit.Stage(user, AuditActions.Update, FolderEntity, id.ToString(), clean, "name", before, clean, "");
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException error) when (EvaluationWrites.Duplicate(error))
        { return new(false, StatusCodes.Status409Conflict, "มีโฟลเดอร์ชื่อนี้อยู่แล้ว"); }
        return new(true, StatusCodes.Status200OK, $"เปลี่ยนชื่อเป็น {clean} แล้ว");
    }

    public async Task<SharedResult> TrashFolderAsync(AppUser user, long id, CancellationToken token)
    {
        if (!CanWrite(user)) return new(false, StatusCodes.Status403Forbidden, "บัญชีนี้ไม่มีสิทธิ์แก้ไขโฟลเดอร์");
        var folder = await db.SharedFolders.FirstOrDefaultAsync(row => row.Id == id, token);
        if (folder is null || (await OpenFolderAsync(id, token)).Problem is not null)
            return new(false, StatusCodes.Status404NotFound, "ไม่พบโฟลเดอร์นี้");
        if (!CanChange(user, folder.CreatedById))
            return new(false, StatusCodes.Status403Forbidden, "ลบได้เฉพาะผู้สร้างโฟลเดอร์ หรือ Supervisor ขึ้นไป");

        folder.DeletedAt = DateTimeOffset.UtcNow;
        folder.DeletedBy = Who(user);
        audit.Stage(user, AuditActions.Trash, FolderEntity, id.ToString(), folder.Name, "deleted_at", "", folder.DeletedAt.Value.ToString("O"), "");
        await db.SaveChangesAsync(token);
        return new(true, StatusCodes.Status200OK, $"ย้ายโฟลเดอร์ {folder.Name} ลงถังขยะแล้ว");
    }

    /* ------------------------------------------------------------------ files */

    public async Task<SharedResult> UploadAsync(AppUser user, long? folderId, IFormFile? file, CancellationToken token)
    {
        if (!CanWrite(user)) return new(false, StatusCodes.Status403Forbidden, "บัญชีนี้ไม่มีสิทธิ์อัปโหลดไฟล์");
        if (!files.Configured) return new(false, StatusCodes.Status503ServiceUnavailable, "ยังไม่ได้ตั้งค่าที่เก็บไฟล์");
        if (file is null) return new(false, StatusCodes.Status400BadRequest, "ต้องแนบไฟล์");
        if (file.Length == 0) return new(false, StatusCodes.Status400BadRequest, "ไฟล์ว่าง");
        if (file.Length > SharedFiles.MaxBytes) return new(false, StatusCodes.Status413PayloadTooLarge, "ไฟล์ใหญ่เกิน 32 MB");
        var wanted = SharedFiles.UploadName(file.FileName);
        if (!SharedFiles.Allowed(wanted))
            return new(false, StatusCodes.Status415UnsupportedMediaType, "ไม่รับไฟล์ชนิดนี้ — รับ PDF, Word, Excel, PowerPoint, รูปภาพ, CSV/TXT และ ZIP");
        var (folder, _, missing) = await OpenFolderAsync(folderId, token);
        if (missing is not null) return new(false, StatusCodes.Status404NotFound, missing);

        var taken = await db.SharedFiles.Where(row => row.FolderId == folderId && row.DeletedAt == null)
            .Select(row => row.Name).ToListAsync(token);
        var name = SharedFiles.FreeName(wanted, taken);
        var key = BlobPaths.ForShared(folderId, name);
        // Never trust the upload's claimed MIME type when returning content to another browser.
        var contentType = InlineViewing.TypeFor(name) ?? "application/octet-stream";

        await using (var stream = file.OpenReadStream())
        {
            // Blob metadata headers are ASCII-only: the name people read stays in the row.
            await files.PutAsync(key, stream, contentType, new Dictionary<string, string>
            {
                ["originalName"] = BlobPaths.SafeName(name),
                ["scope"] = "shared",
                ["uploadedBy"] = BlobPaths.SafeName(user.Email),
            }, token);
        }

        SharedFile row = null!;
        try
        {
            await EvaluationWrites.InOneAsync(db, async () =>
            {
                row = new SharedFile
                {
                    FolderId = folderId, Name = name, ContentType = contentType, SizeBytes = file.Length, ObjectKey = key,
                    UploadedById = user.UserId, UploadedBy = Who(user), UploadedAt = DateTimeOffset.UtcNow,
                };
                db.SharedFiles.Add(row);
                await db.SaveChangesAsync(token);
                audit.Stage(user, AuditActions.Upload, FileEntity, row.Id.ToString(), name,
                    "folder", "", folder?.Name ?? "ชั้นบนสุด", "");
                await db.SaveChangesAsync(token);
            }, token);
        }
        catch (DbUpdateException error) when (EvaluationWrites.Duplicate(error))
        { return new(false, StatusCodes.Status409Conflict, "มีไฟล์ชื่อนี้เพิ่งถูกอัปโหลด ลองอีกครั้ง"); }
        var said = name == wanted ? $"อัปโหลด {name} แล้ว" : $"มีไฟล์ชื่อ {wanted} อยู่แล้ว จึงเก็บเป็น {name}";
        return new(true, StatusCodes.Status200OK, said, new { id = row.Id, name, message = said });
    }

    public async Task<SharedResult> RenameFileAsync(AppUser user, long id, string? name, CancellationToken token)
    {
        if (!CanWrite(user)) return new(false, StatusCodes.Status403Forbidden, "บัญชีนี้ไม่มีสิทธิ์แก้ไขไฟล์");
        var file = await db.SharedFiles.FirstOrDefaultAsync(row => row.Id == id, token);
        if (file is null || file.DeletedAt is not null || (await OpenFolderAsync(file.FolderId, token)).Problem is not null)
            return new(false, StatusCodes.Status404NotFound, "ไม่พบไฟล์นี้");
        if (!CanChange(user, file.UploadedById))
            return new(false, StatusCodes.Status403Forbidden, "เปลี่ยนชื่อได้เฉพาะผู้อัปโหลด หรือ Supervisor ขึ้นไป");
        if (SharedFiles.NameProblem(name, SharedFiles.MaxFileName) is { } problem) return new(false, StatusCodes.Status400BadRequest, problem);

        // The type stays what it is: a name typed without the extension keeps the file's own.
        var clean = name!.Trim();
        var extension = Path.GetExtension(file.Name);
        if (!string.Equals(Path.GetExtension(clean), extension, StringComparison.OrdinalIgnoreCase)) clean += extension;
        if (clean.Length > SharedFiles.MaxFileName) return new(false, StatusCodes.Status400BadRequest, $"ชื่อยาวเกิน {SharedFiles.MaxFileName} ตัวอักษร");
        if (clean == file.Name) return new(true, StatusCodes.Status200OK, "ชื่อเดิม ไม่มีอะไรเปลี่ยน");
        if (await db.SharedFiles.AnyAsync(row => row.Id != id && row.FolderId == file.FolderId && row.DeletedAt == null && row.Name == clean, token))
            return new(false, StatusCodes.Status409Conflict, "มีไฟล์ชื่อนี้อยู่แล้ว");

        var before = file.Name;
        file.Name = clean;
        audit.Stage(user, AuditActions.Update, FileEntity, id.ToString(), clean, "name", before, clean, "");
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        catch (DbUpdateException error) when (EvaluationWrites.Duplicate(error))
        { return new(false, StatusCodes.Status409Conflict, "มีไฟล์ชื่อนี้อยู่แล้ว"); }
        return new(true, StatusCodes.Status200OK, $"เปลี่ยนชื่อเป็น {clean} แล้ว");
    }

    public async Task<SharedResult> TrashFileAsync(AppUser user, long id, CancellationToken token)
    {
        if (!CanWrite(user)) return new(false, StatusCodes.Status403Forbidden, "บัญชีนี้ไม่มีสิทธิ์แก้ไขไฟล์");
        var file = await db.SharedFiles.FirstOrDefaultAsync(row => row.Id == id, token);
        if (file is null || file.DeletedAt is not null || (await OpenFolderAsync(file.FolderId, token)).Problem is not null)
            return new(false, StatusCodes.Status404NotFound, "ไม่พบไฟล์นี้");
        if (!CanChange(user, file.UploadedById))
            return new(false, StatusCodes.Status403Forbidden, "ลบได้เฉพาะผู้อัปโหลด หรือ Supervisor ขึ้นไป");

        file.DeletedAt = DateTimeOffset.UtcNow;
        file.DeletedBy = Who(user);
        audit.Stage(user, AuditActions.Trash, FileEntity, id.ToString(), file.Name, "deleted_at", "", file.DeletedAt.Value.ToString("O"), "");
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        return new(true, StatusCodes.Status200OK, $"ย้าย {file.Name} ลงถังขยะแล้ว");
    }

    /* ------------------------------------------------------------------ restoring */

    /// <summary>
    /// Brings an item back where it was. Refused while the folder it lived in is itself in the trash — restoring into
    /// a folder nobody can open would be a second disappearance. A name taken since is given the next free one.
    /// </summary>
    public async Task<SharedResult> RestoreAsync(AppUser user, string? kind, long id, CancellationToken token)
    {
        if (!CanRestore(user)) return new(false, StatusCodes.Status403Forbidden, "กู้คืนได้เฉพาะ Supervisor ขึ้นไป");
        if (kind == "folder")
        {
            var folder = await db.SharedFolders.FirstOrDefaultAsync(row => row.Id == id, token);
            if (folder is null) return new(false, StatusCodes.Status404NotFound, "ไม่พบโฟลเดอร์นี้");
            if (folder.DeletedAt is null) return new(false, StatusCodes.Status409Conflict, "โฟลเดอร์นี้ไม่ได้อยู่ในถังขยะ");
            if ((await OpenFolderAsync(folder.ParentId, token)).Problem is not null)
                return new(false, StatusCodes.Status409Conflict, "โฟลเดอร์ที่เก็บรายการนี้อยู่ในถังขยะด้วย — กู้โฟลเดอร์นั้นก่อน");
            var taken = await db.SharedFolders.Where(row => row.ParentId == folder.ParentId && row.DeletedAt == null)
                .Select(row => row.Name).ToListAsync(token);
            var before = folder.Name;
            folder.Name = SharedFiles.FreeName(folder.Name, taken, SharedFiles.MaxFolderName, false);
            folder.DeletedAt = null;
            folder.DeletedBy = "";
            audit.Stage(user, AuditActions.Restore, FolderEntity, id.ToString(), folder.Name, "name", before, folder.Name, "");
            await db.SaveChangesAsync(token);
            return new(true, StatusCodes.Status200OK, $"กู้โฟลเดอร์ {folder.Name} คืนแล้ว");
        }
        if (kind == "file")
        {
            var file = await db.SharedFiles.FirstOrDefaultAsync(row => row.Id == id, token);
            if (file is null) return new(false, StatusCodes.Status404NotFound, "ไม่พบไฟล์นี้");
            if (file.DeletedAt is null) return new(false, StatusCodes.Status409Conflict, "ไฟล์นี้ไม่ได้อยู่ในถังขยะ");
            if ((await OpenFolderAsync(file.FolderId, token)).Problem is not null)
                return new(false, StatusCodes.Status409Conflict, "โฟลเดอร์ที่เก็บไฟล์นี้อยู่ในถังขยะ — กู้โฟลเดอร์นั้นก่อน");
            var taken = await db.SharedFiles.Where(row => row.FolderId == file.FolderId && row.DeletedAt == null)
                .Select(row => row.Name).ToListAsync(token);
            var before = file.Name;
            file.Name = SharedFiles.FreeName(file.Name, taken);
            file.DeletedAt = null;
            file.DeletedBy = "";
            audit.Stage(user, AuditActions.Restore, FileEntity, id.ToString(), file.Name, "name", before, file.Name, "");
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException) { return Conflict(); }
            return new(true, StatusCodes.Status200OK, $"กู้ไฟล์ {file.Name} คืนแล้ว");
        }
        return new(false, StatusCodes.Status400BadRequest, "ระบุชนิดเป็น folder หรือ file");
    }
}
