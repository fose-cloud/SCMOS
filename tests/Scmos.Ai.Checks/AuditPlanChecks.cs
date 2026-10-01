using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// Audit Planning and a new subcontractor's onboarding (1 Oct 2026): the plan's lines, the sheet's marks, the
/// vendor's audit date written onto the same plan, and the thirteen-document checklist. A throwaway LocalDB.
/// </summary>
static class AuditPlanChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        check(VendorOnboarding.Items.Length == 13 && VendorOnboarding.Items.Select(item => item.No).SequenceEqual(Enumerable.Range(1, 13))
            && VendorOnboarding.Items.Select(item => item.Code).Distinct().Count() == 13
            && new[] { 3, 6, 7, 8 }.All(no => SupplierCompliance.Match(VendorOnboarding.Items[no - 1].Kind) is not null)
            && VendorOnboarding.Items.All(item => BlobPaths.SupplierFolders.Contains(item.Folder)),
            "onboarding: the department's thirteen documents in order; the affidavit, licence and both insurances are the register's own required documents");
        var today = Formats.DateNumber("01/10/2026");
        check(AuditPlanRules.NotYetDone(AuditPlanRules.Planned, "30/09/2026", today)
            && !AuditPlanRules.NotYetDone(AuditPlanRules.Planned, "01/10/2026", today)
            && !AuditPlanRules.NotYetDone(AuditPlanRules.Done, "13/02/2026", today)
            && !AuditPlanRules.NotYetDone(AuditPlanRules.Postponed, "13/02/2026", today),
            "audit plan: X is a planned audit whose date has passed — read, never stored");
        check(!CarrierBoundary.Allows("GET", new PathString("/api/audit-plan"))
            && !CarrierBoundary.Allows("GET", new PathString("/api/suppliers/4/onboarding"))
            && !CarrierBoundary.Allows("PUT", new PathString("/api/suppliers/4/audit-date")),
            "audit plan: a carrier reaches neither the plan nor any vendor's checklist");
        if (sql) await SqlAsync(check);
    }

    private static AuditItemInput Line(string company, string date, string kind = AuditPlanRules.ReAudit, string status = AuditPlanRules.Planned,
        string next = "", int? supplierId = null) =>
        new(kind, supplierId, company, "lcb", "Nattikorn\n  Salarnyou \n\nPunnarai", date, AuditPlanRules.Fixed, status, next, "", "", "", null);

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_AUDIT_PLAN_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var db = new ScmosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var dgt = new Supplier { Name = "DGT", Code = "DGT", LegalName = "DGT Cross Haul Co., Ltd.", Status = "approved", IsCarrier = true, CreatedAt = now, UpdatedAt = now };
            var fresh = new Supplier { Name = "NORTH STAR", Code = "NST", LegalName = "North Star Transport Co., Ltd.", Status = "pending-audit", IsCarrier = true, CreatedAt = now, UpdatedAt = now };
            db.Suppliers.AddRange(dgt, fresh);
            await db.SaveChangesAsync();

            var audit = new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
            var plans = new AuditPlanService(db, new SupplierNames(db), audit);
            var user = new AppUser("o", "o@test.invalid", "O", Roles.Operation, "OP-C1", "test", true);

            var first = await plans.AddItemAsync(user, Line("DGT Cross Haul Co., Ltd.", "13/02/2026"), default);
            var second = await plans.AddItemAsync(user, Line("Unlisted Haulage Co., Ltd.", "15/07/2026"), default);
            var badDate = await plans.AddItemAsync(user, Line("DGT", "2026-02-13"), default);
            var noNext = await plans.AddItemAsync(user, Line("DGT", "13/03/2026", status: AuditPlanRules.Postponed), default);
            var nextYear = await plans.AddItemAsync(user, Line("DGT", "20/01/2027"), default);
            var plan = await plans.ReadAsync(2026, default);
            var dgtLine = plan.Items.Single(item => item.Id == first.Id);
            var other = plan.Items.Single(item => item.Id == second.Id);
            check(first.Ok && second.Ok && !badDate.Ok && !noNext.Ok && nextYear.Ok
                && dgtLine.SupplierId == dgt.Id && dgtLine.Company == "DGT" && dgtLine.Target == "LCB"
                && dgtLine.PersonInCharge == "Nattikorn\nSalarnyou\nPunnarai" && dgtLine.Sequence == 1 && other.Sequence == 2
                && other.SupplierId is null && other.Company == "Unlisted Haulage Co., Ltd."
                && plan.Items.Count == 2 && plan.Years.Contains(2027) && plan.Plan.Title == AuditPlanService.DefaultTitle(2026),
                "audit plan: a line links to the register when the company is in it, keeps the name when not, numbers itself in its section, and files under its date's year");

            var moved = await plans.UpdateItemAsync(user, first.Id!.Value, Line("DGT", "13/02/2026", status: AuditPlanRules.Postponed, next: "10/04/2026"), default);
            var header = await plans.SaveHeaderAsync(user, 2026, new AuditPlanHeaderInput("", "Nipaporn", "Nattikorn", "22/12/2025", "", "Veerasak", ""), default);
            var badHeader = await plans.SaveHeaderAsync(user, 2026, new AuditPlanHeaderInput("", "", "", "22.12.2025", "", "", ""), default);
            var after = await plans.ReadAsync(2026, default);
            check(moved.Ok && after.Items.Single(item => item.Id == first.Id).NextDate == "10/04/2026"
                && header.Ok && !badHeader.Ok && after.Plan.PreparedBy == "Nipaporn" && after.Plan.ReviewedDate == "22/12/2025"
                && after.Plan.ApprovedBy == "Veerasak" && after.Plan.Revision == "00",
                "audit plan: a postponed line keeps where it moved to; the sign-off boxes save as the sheet has them");

            var imported = await plans.ImportAsync(user, [Line("DGT", "13/02/2026"), Line("JTC Logistics Co., Ltd.", "19/03/2026")], default);
            check(imported.Saved == 1 && imported.Skipped == 1 && imported.Errors.Count == 0,
                "audit plan: a pasted line the plan already holds — the same company on the same date — is skipped");

            var set = await plans.SetVendorAuditAsync(user, fresh.Id, "05/11/2026", "Nattikorn", "LCB", default);
            var reset = await plans.SetVendorAuditAsync(user, fresh.Id, "12/11/2026", null, null, default);
            var newLines = await db.AuditPlanItems.AsNoTracking().Where(row => row.SupplierId == fresh.Id).ToListAsync();
            check(set.Ok && reset.Ok && newLines.Count == 1 && newLines[0].Kind == AuditPlanRules.New
                && newLines[0].AuditDate == "12/11/2026" && newLines[0].PersonInCharge == "Nattikorn" && newLines[0].Company == "NORTH STAR",
                "onboarding: a new vendor's audit date is one 'new' line on the plan, moved when the date changes");

            var done = await plans.SetOnboardingAsync(user, fresh.Id, "code-of-conduct", "done", "You can send it back after the audit.", default);
            var bad = await plans.SetOnboardingAsync(user, fresh.Id, "code-of-conduct", "maybe", null, default);
            var unknown = await plans.SetOnboardingAsync(user, fresh.Id, "nothing", "done", null, default);
            db.Documents.Add(new StoredDocument { Scope = "supplier", SupplierId = fresh.Id, Folder = "Insurance", Kind = "insurance-cargo",
                FileName = "cargo.pdf", ObjectKey = "SCMOS/Supplier/NST/Insurance/cargo.pdf", ExpiryDate = "31/12/2027", UploadedBy = "o", UploadedAt = now });
            await db.SaveChangesAsync();
            var view = await plans.OnboardingAsync(fresh.Id, default);
            var conduct = view!.Items.Single(item => item.Code == "code-of-conduct");
            var cargo = view.Items.Single(item => item.No == 8);
            check(done.Ok && !bad.Ok && !unknown.Ok && view.Done == 1 && view.Total == 13
                && conduct.Status == VendorOnboarding.Done && conduct.Remark == "You can send it back after the audit."
                && cargo.Expires && cargo.Files.Single().FileName == "cargo.pdf" && view.Audit?.AuditDate == "12/11/2026"
                && view.Items.Where(item => item.Code != "code-of-conduct").All(item => item.Status == VendorOnboarding.Pending),
                "onboarding: each line is Done or Pending with a remark, its files are the supplier's own of that kind, and the audit date is the plan's");

            var removed = await plans.DeleteItemAsync(user, second.Id!.Value, default);
            check(removed.Ok && !await db.AuditPlanItems.AnyAsync(row => row.Id == second.Id)
                && await db.AuditEvents.CountAsync(row => row.Entity == "audit-plan-item") == 8
                && await db.AuditEvents.CountAsync(row => row.Entity == "audit-plan") == 1
                && await db.AuditEvents.CountAsync(row => row.Entity == "supplier-onboarding") == 1,
                "audit plan: a line removed is gone from the plan and kept in the audit trail, with every other change");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
