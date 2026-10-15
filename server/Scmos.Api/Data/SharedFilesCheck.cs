using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// The shared file space (14 Oct 2026): <c>--check-shared-files [--local-db]</c>.
///
/// The rules on their own, then the service end to end against a database it creates and drops — by default on the
/// isolated LocalDB instance the existing checks use, never a shared one — with an in-memory file store, so no byte reaches
/// Blob Storage and no row reaches Production.
/// </summary>
public static class SharedFilesCheck
{
    private const string LocalSource = "(localdb)\\ScmosAiAuditCheck_20260907";

    public static async Task<int?> RunAsync(string[] args)
    {
        var flag = Array.IndexOf(args, "--check-shared-files");
        if (flag < 0) return null;
        if (args.Skip(flag + 1).Any(value => value != "--local-db"))
        {
            Console.WriteLine("Only --local-db is accepted. No configurable database target is allowed.");
            return 2;
        }
        var failed = SharedWorkbookCheck.Run();

        /* -------------------------------------------------------------- rules */
        failed += Check("an empty name is refused", SharedFiles.NameProblem("  ", 120) is not null, true);
        failed += Check("a slash is refused", SharedFiles.NameProblem("A/B", 120) is not null, true);
        failed += Check("dots alone are refused", SharedFiles.NameProblem("..", 120) is not null, true);
        failed += Check("a Thai name is kept", SharedFiles.NameProblem("สัญญาผู้ขนส่ง 2026", 120), null);
        failed += Check("a name past the limit is refused", SharedFiles.NameProblem(new string('a', 121), 120) is not null, true);
        failed += Check("PDF and Excel are taken", SharedFiles.Allowed("a.PDF") && SharedFiles.Allowed("b.xlsx"), true);
        failed += Check("programs and scripts are not",
            SharedFiles.Allowed("setup.exe") || SharedFiles.Allowed("run.bat") || SharedFiles.Allowed("page.html") || SharedFiles.Allowed("x.js"), false);
        failed += Check("an upload keeps only its last path segment", SharedFiles.UploadName("C:\\Users\\me\\ใบแจ้งหนี้.pdf"), "ใบแจ้งหนี้.pdf");
        failed += Check("reserved characters in an upload become underscores", SharedFiles.UploadName("a:b?.pdf"), "a_b_.pdf");
        const string teams = "https://contoso.sharepoint.com/:f:/r/sites/Team/Shared%20Documents/Folder?d=w0123&csf=1&web=1&e=AbCdEf";
        failed += Check("a SharePoint folder link is handed on unchanged", SharedFiles.TeamsLink("  " + teams + " "), teams);
        failed += Check("no link configured, no button", SharedFiles.TeamsLink(null), null);
        failed += Check("files open in the browser for nobody (withdrawn 15 Oct 2026)", SharedFiles.OpenInBrowser, false);
        failed += Check("a link that is not https, or not SharePoint, is refused",
            new[] { "http://contoso.sharepoint.com/sites/Team", "javascript:alert(1)", "https://contoso.sharepoint.com.evil.example/x",
                "https://sharepoint.com/x", "https://user@contoso.sharepoint.com/x", "https://contoso.sharepoint.com:8443/x", "/sites/Team" }
                .All(link => SharedFiles.TeamsLink(link) is null), true);
        failed += Check("a long upload keeps its extension",
            SharedFiles.UploadName(new string('x', 300) + ".pdf") is { Length: SharedFiles.MaxFileName } kept && kept.EndsWith(".pdf"), true);
        failed += Check("a second copy is numbered", SharedFiles.FreeName("a.pdf", ["A.pdf", "a (2).pdf"]), "a (3).pdf");
        failed += Check("a free name is left alone", SharedFiles.FreeName("b.pdf", ["a.pdf"]), "b.pdf");
        var longFolder = new string('a', SharedFiles.MaxFolderName);
        failed += Check("a restored folder's suffix stays within its own name limit",
            SharedFiles.FreeName(longFolder, [longFolder], SharedFiles.MaxFolderName, false).Length <= SharedFiles.MaxFolderName, true);
        failed += Check("shared files live outside the archived SCMOS/ tree",
            BlobPaths.ForShared(7, "x.pdf").StartsWith("Shared/7/") && BlobPaths.ForShared(null, "x.pdf").StartsWith("Shared/top/"), true);

        var carrier = User("carrier", Roles.Subcontractor);
        var viewer = User("viewer", Roles.Viewer);
        var owner = User("owner", Roles.Operation);
        var colleague = User("colleague", Roles.Operation);
        var supervisor = User("supervisor", Roles.Supervisor);
        failed += Check("a carrier sees nothing, though it holds the upload right",
            carrier.Can(Capability.UploadDocuments) && !SharedFilesService.CanSee(carrier) && !SharedFilesService.CanWrite(carrier), true);
        failed += Check("a viewer sees but does not write", SharedFilesService.CanSee(viewer) && !SharedFilesService.CanWrite(viewer), true);
        failed += Check("only a supervisor restores",
            SharedFilesService.CanRestore(supervisor) && !SharedFilesService.CanRestore(owner), true);
        failed += Check("unrecognized identities, missing ids and unknown roles cannot read the space",
            !SharedFilesService.CanSee(owner with { Recognised = false })
                && !SharedFilesService.CanSee(owner with { UserId = "" })
                && !SharedFilesService.CanSee(owner with { Role = "unknown" }), true);
        failed += Check("carrier whitespace and casing never bypass the boundary",
            SharedFilesService.CanSee(carrier with { Role = " subcontractor " }), false);

        await using (var noSql = new ScmosDbContext(new DbContextOptionsBuilder<ScmosDbContext>().Options))
        {
            var guarded = new SharedFilesService(noSql, new MemoryStore(),
                new AuditService(noSql, new HttpContextAccessor(), NullLogger<AuditService>.Instance));
            failed += Check("service reads deny carriers before any database or Blob access",
                (await guarded.ListAsync(carrier, null, default)).Status == 403
                && (await guarded.TrashAsync(carrier, default)).Status == 403
                && (await guarded.OpenAsync(carrier, 1, default)).Refusal?.Status == 403, true);
            failed += Check("editor denies carriers and unauthorized writes before SQL",
                (await guarded.EditorAsync(carrier, 1, default)).Status == 403
                && (await guarded.EditCellsAsync(viewer, 1, "version", [], default)).Status == 403
                && (await guarded.EditContentAsync(carrier, 1, "version", null, default)).Status == 403, true);
            failed += Check("service mutations deny carriers before database access",
                (await guarded.RenameFolderAsync(carrier, 1, "x", default)).Status == 403
                && (await guarded.TrashFolderAsync(carrier, 1, default)).Status == 403
                && (await guarded.RenameFileAsync(carrier, 1, "x", default)).Status == 403
                && (await guarded.TrashFileAsync(carrier, 1, default)).Status == 403
                && (await guarded.RestoreAsync(carrier, "file", 1, default)).Status == 403, true);
        }

        // CI runs only deterministic rules. Actual SQL needs this explicit, fixed-local-target opt-in.
        if (!args.Contains("--local-db"))
        {
            Console.WriteLine(failed == 0 ? "All shared-file rule checks passed (no database or Blob calls)." : $"{failed} shared-file check(s) failed.");
            return failed == 0 ? 0 : 1;
        }

        /* -------------------------------------------------------------- service, on a scratch database */
        var database = "SCMOS_SharedFilesCheck_" + Guid.NewGuid().ToString("N");
        var connection = $"Server={LocalSource};Database={database};Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection).Options;
        var ownsDatabase = false;
        try
        {
        await using (var setup = new ScmosDbContext(options))
        {
            if (await setup.Database.CanConnectAsync()) throw new InvalidOperationException("Scratch database already exists.");
            ownsDatabase = true;
            await setup.Database.MigrateAsync();
        }
        var store = new MemoryStore();
        async Task<T> With<T>(Func<SharedFilesService, Task<T>> act)
        {
            await using var db = new ScmosDbContext(options);
            return await act(new SharedFilesService(db, store, new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance)));
        }
        var none = CancellationToken.None;

        var contracts = await With(s => s.CreateFolderAsync(owner, null, "Contracts", none));
        var contractsId = Id(contracts);
        failed += Check("staff with the upload right make a folder", contracts.Ok, true);
        failed += Check("the same name in another case is the same folder",
            (await With(s => s.CreateFolderAsync(colleague, null, "contracts", none))).Status, 409);
        failed += Check("a viewer cannot make a folder", (await With(s => s.CreateFolderAsync(viewer, null, "Mine", none))).Status, 403);

        long? deepest = contractsId;
        var depthRefused = 0;
        for (var level = 2; level <= SharedFiles.MaxDepth + 1; level++)
        {
            var made = await With(s => s.CreateFolderAsync(owner, deepest, "L" + level, none));
            if (made.Ok) deepest = Id(made); else depthRefused = made.Status;
        }
        failed += Check($"folders nest {SharedFiles.MaxDepth} deep and no deeper", depthRefused, 400);

        var first = await With(s => s.UploadAsync(owner, contractsId, Form("a.pdf", "first"), none));
        var second = await With(s => s.UploadAsync(owner, contractsId, Form("A.pdf", "second"), none));
        failed += Check("a file is uploaded", first.Ok, true);
        failed += Check("a second file of the same name is kept beside it, numbered", Name(second), "A (2).pdf");
        failed += Check("a program is refused", (await With(s => s.UploadAsync(owner, contractsId, Form("tool.exe", "x"), none))).Status, 415);
        failed += Check("an empty file is refused", (await With(s => s.UploadAsync(owner, contractsId, Form("e.pdf", ""), none))).Status, 400);
        failed += Check("a file over 32 MB is refused before it is read",
            (await With(s => s.UploadAsync(owner, contractsId, Form("big.pdf", "x", SharedFiles.MaxBytes + 1), none))).Status, 413);
        failed += Check("the bytes went to the shared tree, nowhere else",
            store.Keys.Count == 2 && store.Keys.All(key => key.StartsWith($"Shared/{contractsId}/")), true);

        var listed = Json((await With(s => s.ListAsync(colleague, contractsId, none))).Body);
        failed += Check("the folder lists its two files and its subfolder",
            listed.GetProperty("files").GetArrayLength() == 2 && listed.GetProperty("folders").GetArrayLength() == 1, true);
        failed += Check("a colleague may not change the owner's file",
            listed.GetProperty("files").EnumerateArray().All(file => !file.GetProperty("canChange").GetBoolean()), true);
        failed += Check("no file is offered for opening or editing in the browser",
            listed.GetProperty("files").EnumerateArray().All(file =>
                !file.GetProperty("online").GetBoolean() && !file.GetProperty("viewable").GetBoolean()), true);
        failed += Check("with no Teams link configured the listing offers none",
            listed.GetProperty("teamsUrl").ValueKind == JsonValueKind.Null, true);
        await using (var linkDb = new ScmosDbContext(options))
        {
            var configured = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["SharedFiles:TeamsUrl"] = teams }).Build();
            var linked = new SharedFilesService(linkDb, store,
                new AuditService(linkDb, new HttpContextAccessor(), NullLogger<AuditService>.Instance), configured);
            failed += Check("the configured Teams link reaches a staff listing",
                Json((await linked.ListAsync(viewer, null, none)).Body).GetProperty("teamsUrl").GetString(), teams);
        }
        var firstId = Id(first) ?? 0;
        var editor = Json((await With(s => s.EditorAsync(owner, firstId, none))).Body);
        var version = editor.GetProperty("version").GetString();
        failed += Check("a viewer cannot edit, and is told it may only read",
            (await With(s => s.EditContentAsync(viewer, firstId, version, Form("a.pdf", "%PDF-1.4\nnew\n%%EOF"), none))).Status == 403
                && !Json((await With(s => s.EditorAsync(viewer, firstId, none))).Body).GetProperty("canEdit").GetBoolean(), true);
        failed += Check("a colleague with the upload right may edit another uploader's file",
            Json((await With(s => s.EditorAsync(colleague, firstId, none))).Body).GetProperty("canEdit").GetBoolean(), true);
        var editResults = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => With(s =>
            s.EditContentAsync(i == 0 ? owner : colleague, firstId, version, Form("a.pdf", $"%PDF-1.4\nchange {i}\n%%EOF"), none))));
        failed += Check("two writers on the same version produce exactly one new revision and one conflict",
            editResults.Count(r => r.Ok) == 1 && editResults.Count(r => r.Status == 409) == 1, true);
        var editedInfo = Json((await With(s => s.EditorAsync(owner, firstId, none))).Body);
        failed += Check("the editor lists immutable previous content",
            editedInfo.GetProperty("revision").GetInt32() == 2 && editedInfo.GetProperty("history").GetArrayLength() == 1, true);
        var previousContent = await With(s => s.OpenAsync(owner, firstId, none, 1));
        failed += Check("historical download keeps the original exact bytes",
            previousContent.Content is not null && new StreamReader(previousContent.Content).ReadToEnd() == "first", true);
        previousContent.Content?.Dispose();
        failed += Check("invalid image/PDF bytes and XLSX binary replacement are refused",
            (await With(s => s.EditContentAsync(owner, firstId, editedInfo.GetProperty("version").GetString(), Form("a.pdf", "not PDF"), none))).Status, 415);
        failed += Check("a colleague cannot rename it", (await With(s => s.RenameFileAsync(colleague, firstId, "b.pdf", none))).Status, 403);
        failed += Check("a supervisor can, and a name typed without its extension keeps the file's own",
            (await With(s => s.RenameFileAsync(supervisor, firstId, "สัญญา", none))).Ok
                && Json((await With(s => s.ListAsync(owner, contractsId, none))).Body).GetProperty("files").EnumerateArray()
                    .Any(file => file.GetProperty("name").GetString() == "สัญญา.pdf"), true);

        failed += Check("the owner moves a file to the trash", (await With(s => s.TrashFileAsync(owner, firstId, none))).Ok, true);
        failed += Check("a trashed file is out of the folder and cannot be opened",
            Json((await With(s => s.ListAsync(owner, contractsId, none))).Body).GetProperty("files").GetArrayLength() == 1
                && (await With(s => s.OpenAsync(owner, firstId, none))).Refusal?.Status == 404, true);
        failed += Check("it is in the trash", Json((await With(s => s.TrashAsync(owner, none))).Body).GetProperty("items")
            .EnumerateArray().Any(item => item.GetProperty("kind").GetString() == "file" && item.GetProperty("id").GetInt64() == firstId), true);
        failed += Check("the owner cannot restore", (await With(s => s.RestoreAsync(owner, "file", firstId, none))).Status, 403);
        failed += Check("a supervisor restores it", (await With(s => s.RestoreAsync(supervisor, "file", firstId, none))).Ok, true);

        var secondId = Id(second) ?? 0;
        failed += Check("a supervisor moves anybody's file to the trash", (await With(s => s.TrashFileAsync(supervisor, secondId, none))).Ok, true);
        failed += Check("the folder goes to the trash with what is in it", (await With(s => s.TrashFolderAsync(owner, contractsId!.Value, none))).Ok, true);
        failed += Check("what was inside is out of sight with it, and cannot be trashed on its own",
            (await With(s => s.ListAsync(owner, contractsId, none))).Status == 404
                && (await With(s => s.OpenAsync(owner, firstId, none))).Refusal is not null
                && (await With(s => s.TrashFileAsync(supervisor, firstId, none))).Status == 404, true);
        failed += Check("a file trashed separately cannot come back into a trashed folder",
            (await With(s => s.RestoreAsync(supervisor, "file", secondId, none))).Status, 409);
        var replacement = await With(s => s.CreateFolderAsync(colleague, null, "Contracts", none));
        failed += Check("the name of a trashed folder is free again", replacement.Ok, true);
        var restored = await With(s => s.RestoreAsync(supervisor, "folder", contractsId!.Value, none));
        failed += Check("a restored folder whose name was taken since gets the next free one",
            restored.Ok && restored.Message.Contains("Contracts (2)"), true);
        var opened = await With(s => s.OpenAsync(owner, firstId, none));
        failed += Check("its files open again, with the bytes that were uploaded",
            opened.Content is not null && new StreamReader(opened.Content).ReadToEnd().StartsWith("%PDF-1.4\nchange"), true);
        opened.Content?.Dispose();
        var currentEditor = Json((await With(s => s.EditorAsync(owner, firstId, none))).Body);
        var currentVersion = currentEditor.GetProperty("version").GetString();
        await With(s => s.RenameFileAsync(owner, firstId, "another name", none));
        failed += Check("renaming while an editor is open invalidates its save token",
            (await With(s => s.EditContentAsync(owner, firstId, currentVersion, Form("a.pdf", "%PDF-1.4\nnew\n%%EOF"), none))).Status, 409);

        // Fail the audit AFTER the entity's first insert to prove the existing transaction rolls both back.
        var faultOptions = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection)
            .AddInterceptors(new FailSharedAudit()).Options;
        foreach (var kind in new[] { "folder", "file" })
        {
            var refused = false;
            await using (var faultDb = new ScmosDbContext(faultOptions))
            {
                var faultService = new SharedFilesService(faultDb, store,
                    new AuditService(faultDb, new HttpContextAccessor(), NullLogger<AuditService>.Instance));
                try
                {
                    if (kind == "folder") await faultService.CreateFolderAsync(owner, null, "Audit failure", none);
                    else await faultService.UploadAsync(owner, null, Form("audit-failure.pdf", "fake test bytes"), none);
                }
                catch (InvalidOperationException error) when (error.Message == "Synthetic shared audit failure") { refused = true; }
            }
            await using var verification = new ScmosDbContext(options);
            failed += Check($"{kind} metadata cannot commit without its audit",
                refused && !await verification.SharedFolders.AnyAsync(row => row.Name == "Audit failure")
                    && !await verification.SharedFiles.AnyAsync(row => row.Name == "audit-failure.pdf"), true);
        }
        var beforeFailure = Json((await With(s => s.EditorAsync(owner, firstId, none))).Body);
        var editAuditFailed = false;
        await using (var faultDb = new ScmosDbContext(faultOptions))
        {
            var faultService = new SharedFilesService(faultDb, store, new AuditService(faultDb, new HttpContextAccessor(), NullLogger<AuditService>.Instance));
            try { await faultService.EditContentAsync(owner, firstId, beforeFailure.GetProperty("version").GetString(), Form("a.pdf", "%PDF-1.4\nfailed audit\n%%EOF"), none); }
            catch (InvalidOperationException e) when (e.Message == "Synthetic shared audit failure") { editAuditFailed = true; }
        }
        var afterFailure = Json((await With(s => s.EditorAsync(owner, firstId, none))).Body);
        failed += Check("a failed audit cannot advance the file or add a revision",
            editAuditFailed && beforeFailure.GetProperty("version").GetString() == afterFailure.GetProperty("version").GetString()
                && afterFailure.GetProperty("history").GetArrayLength() == 1, true);

        var workbookBytes = SharedWorkbookCheck.ValidFixture();
        var workbookUpload = new FormFile(new MemoryStream(workbookBytes), 0, workbookBytes.Length, "file", "rates.xlsx")
            { Headers = new HeaderDictionary(), ContentType = "text/html" };
        var workbookId = Id(await With(s => s.UploadAsync(owner, null, workbookUpload, none)))!.Value;
        var workbookInfo = Json((await With(s => s.EditorAsync(owner, workbookId, none))).Body);
        failed += Check("XLSX preview and scalar changes use the same versioned save service",
            workbookInfo.GetProperty("kind").GetString() == "excel"
            && (await With(s => s.EditCellsAsync(owner, workbookId, workbookInfo.GetProperty("version").GetString(),
                [new("Rates", "A1", "number", "99.5")], none))).Ok, true);
        var workbookChanged = Json((await With(s => s.EditorAsync(owner, workbookId, none))).Body);
        failed += Check("the current XLSX contains the edited price and retains a historical download",
            workbookChanged.GetProperty("workbook").GetProperty("sheets")[0].GetProperty("cells").EnumerateArray()
                .Any(c => c.GetProperty("address").GetString() == "A1" && c.GetProperty("value").GetString() == "99.5")
                && workbookChanged.GetProperty("history").GetArrayLength() == 1, true);
        failed += Check("an XLSX cannot be replaced by arbitrary uploaded bytes",
            (await With(s => s.EditContentAsync(owner, workbookId, workbookChanged.GetProperty("version").GetString(),
                Form("rates.xlsx", "arbitrary bytes"), none))).Status, 415);

        await using (var db = new ScmosDbContext(options))
        {
            var actions = await db.AuditEvents.AsNoTracking()
                .Where(row => row.Entity == SharedFilesService.FolderEntity || row.Entity == SharedFilesService.FileEntity)
                .Select(row => row.Action).Distinct().ToListAsync();
            failed += Check("every change is in the audit trail",
                new[] { AuditActions.Create, AuditActions.Upload, AuditActions.Update, AuditActions.Trash, AuditActions.Restore }
                    .All(actions.Contains), true);
            failed += Check("nothing was deleted from the database",
                await db.SharedFiles.CountAsync() == 3 && await db.SharedFolders.CountAsync() >= 3, true);
        }
        }
        finally
        {
            if (ownsDatabase)
            {
                await using var cleanup = new ScmosDbContext(options);
                if (cleanup.Database.GetDbConnection().DataSource != LocalSource
                    || cleanup.Database.GetDbConnection().Database != database
                    || !database.StartsWith("SCMOS_SharedFilesCheck_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsafe storage test cleanup target.");
                await cleanup.Database.EnsureDeletedAsync();
            }
        }

        Console.WriteLine(failed == 0 ? "All shared-file checks passed." : $"{failed} shared-file check(s) failed.");
        return failed == 0 ? 0 : 1;
    }

    private static AppUser User(string id, string role) =>
        new(id, id + "@example.test", "Check, " + id, role, "", "test", true);

    private static IFormFile Form(string name, string text, long? length = null)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new FormFile(new MemoryStream(bytes), 0, length ?? bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(), ContentType = "application/pdf",
        };
    }

    private static JsonElement Json(object? body) => JsonSerializer.SerializeToElement(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static long? Id(SharedResult result) => result.Body is null ? null : Json(result.Body).GetProperty("id").GetInt64();
    private static string Name(SharedResult result) => result.Body is null ? "" : Json(result.Body).GetProperty("name").GetString() ?? "";

    private static int Check<T>(string why, T got, T want)
    {
        var ok = EqualityComparer<T>.Default.Equals(got, want);
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {why}{(ok ? "" : $" — got {got}, want {want}")}");
        return ok ? 0 : 1;
    }

    private sealed class FailSharedAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditEvent>().Any(entry => entry.State == EntityState.Added
                    && entry.Entity.Entity is SharedFilesService.FolderEntity or SharedFilesService.FileEntity))
                throw new InvalidOperationException("Synthetic shared audit failure");
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>The blob container, in memory: what was put, by key.</summary>
    private sealed class MemoryStore : IFileStore
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> blobs = new(StringComparer.Ordinal);
        public IReadOnlyCollection<string> Keys => blobs.Keys.ToArray();
        public bool Configured => true;

        public async Task<string> PutAsync(string objectKey, Stream content, string contentType, IDictionary<string, string> metadata,
            CancellationToken token)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, token);
            blobs[objectKey] = copy.ToArray();
            return "memory://" + objectKey;
        }

        public Task<Stream?> OpenAsync(string objectKey, CancellationToken token) =>
            Task.FromResult<Stream?>(blobs.TryGetValue(objectKey, out var bytes) ? new MemoryStream(bytes) : null);
    }
}
