using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record AuditPlanHeaderView(int Year, string Title, string PreparedBy, string ReviewedBy, string ReviewedDate,
    string SecondReviewedBy, string ApprovedBy, string Revision, string UpdatedBy, DateTimeOffset? UpdatedAt);

/// <param name="NotYetDone">The sheet's X: planned, and its date has passed.</param>
/// <param name="SupplierStatus">The register's status for a linked company — draft · pending-audit · approved …</param>
public record AuditPlanItemView(long Id, int Year, string Kind, int Sequence, int? SupplierId, string Company,
    string SupplierStatus, string Target, string PersonInCharge, string AuditDate, string Schedule, string Status,
    bool NotYetDone, string NextDate, string Remark, string FindingSentDate, string ReportSentDate, string UpdatedBy,
    DateTimeOffset UpdatedAt);

public record AuditPlanView(AuditPlanHeaderView Plan, IReadOnlyList<AuditPlanItemView> Items, IReadOnlyList<int> Years,
    IReadOnlyDictionary<string, string> Sections);

public record AuditPlanHeaderInput(string? Title, string? PreparedBy, string? ReviewedBy, string? ReviewedDate,
    string? SecondReviewedBy, string? ApprovedBy, string? Revision);

public record AuditItemInput(string? Kind, int? SupplierId, string? Company, string? Target, string? PersonInCharge,
    string? AuditDate, string? Schedule, string? Status, string? NextDate, string? Remark, string? FindingSentDate,
    string? ReportSentDate, int? Sequence);

public record AuditPlanResult(bool Ok, string Message, int Status = StatusCodes.Status200OK, long? Id = null);

public record OnboardingFileView(long Id, string FileName, string ExpiryDate, bool CanShow, string UploadedBy, DateTimeOffset UploadedAt);

public record OnboardingItemView(int No, string Code, string Document, string Note, string Folder, string Kind, string Link,
    bool Expires, string Status, string Remark, string UpdatedBy, DateTimeOffset? UpdatedAt, IReadOnlyList<OnboardingFileView> Files);

/// <param name="Audit">The supplier's open "new" audit on the plan, when one is set.</param>
public record OnboardingView(int SupplierId, string Supplier, string SupplierStatus, int Done, int Total,
    IReadOnlyList<OnboardingItemView> Items, AuditPlanItemView? Audit);

/// <summary>
/// The EHS audit plan with the truck subcontractors, and a new subcontractor's onboarding (1 Oct 2026).
///
/// <para>
/// One plan per year, as the department's workbook is: re-audits of the carriers already working, and the
/// audits new subcontractors are approved by — the latter set from Add New Vendor, which writes the same rows
/// the Audit Planning screen draws, so the vendor's calendar and the plan cannot disagree.
/// </para>
///
/// <para>
/// Every change is audited. A plan line can be removed — it is a plan, and a line typed by mistake is a
/// mistake — and the audit row keeps what it said.
/// </para>
/// </summary>
public class AuditPlanService(ScmosDbContext db, SupplierNames names, AuditService audit)
{
    public static string DefaultTitle(int year) => $"{year} AUDIT planning of EHS with Truck Sub-Contractor";

    private static int Today() => Formats.DateNumber(Formats.Now.ToString("dd/MM/yyyy"));

    public async Task<AuditPlanView> ReadAsync(int year, CancellationToken token)
    {
        var plan = await db.AuditPlans.AsNoTracking().FirstOrDefaultAsync(row => row.Year == year, token);
        var items = await db.AuditPlanItems.AsNoTracking().Where(row => row.Year == year).ToListAsync(token);
        var views = await DescribeAsync(items, token);
        var years = (await db.AuditPlans.AsNoTracking().Select(row => row.Year).ToListAsync(token))
            .Concat(await db.AuditPlanItems.AsNoTracking().Select(row => row.Year).Distinct().ToListAsync(token))
            .Append(Formats.Now.Year).Append(year).Distinct().OrderBy(one => one).ToList();
        return new AuditPlanView(Header(plan, year), views
                .OrderBy(row => Array.IndexOf(AuditPlanRules.Kinds, row.Kind)).ThenBy(row => row.Sequence)
                .ThenBy(row => Formats.DateNumber(row.AuditDate)).ToList(),
            years, AuditPlanRules.Sections);
    }

    public async Task<AuditPlanResult> SaveHeaderAsync(AppUser user, int year, AuditPlanHeaderInput input, CancellationToken token)
    {
        if (year is < 2000 or > 2100) return Refused("ปีไม่ถูกต้อง");
        var reviewed = Text(input.ReviewedDate);
        if (reviewed.Length > 0 && Formats.DateNumber(reviewed) == 0) return Refused("วันที่ Review ต้องเป็นรูปแบบ DD/MM/YYYY");
        var plan = await db.AuditPlans.FirstOrDefaultAsync(row => row.Year == year, token);
        var before = plan is null ? "" : $"{plan.Title} · {plan.PreparedBy} · {plan.ReviewedBy} · {plan.SecondReviewedBy} · {plan.ApprovedBy} · Rev.{plan.Revision}";
        if (plan is null) { plan = new AuditPlan { Year = year }; db.AuditPlans.Add(plan); }
        plan.Title = Cut(Text(input.Title).Length > 0 ? Text(input.Title) : DefaultTitle(year), 200);
        plan.PreparedBy = Cut(Text(input.PreparedBy), 120);
        plan.ReviewedBy = Cut(Text(input.ReviewedBy), 120);
        plan.ReviewedDate = reviewed;
        plan.SecondReviewedBy = Cut(Text(input.SecondReviewedBy), 120);
        plan.ApprovedBy = Cut(Text(input.ApprovedBy), 120);
        plan.Revision = Cut(Text(input.Revision).Length > 0 ? Text(input.Revision) : "00", 20);
        plan.UpdatedBy = user.Signature;
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.Update, "audit-plan", year.ToString(), plan.Title, "header", before,
            $"{plan.Title} · {plan.PreparedBy} · {plan.ReviewedBy} · {plan.SecondReviewedBy} · {plan.ApprovedBy} · Rev.{plan.Revision}", "", token);
        return new AuditPlanResult(true, $"บันทึกหัวแผน {year} แล้ว");
    }

    public async Task<AuditPlanResult> AddItemAsync(AppUser user, AuditItemInput input, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var item = new AuditPlanItem { CreatedBy = user.Signature, CreatedAt = now, UpdatedBy = user.Signature, UpdatedAt = now };
        var problem = await ApplyAsync(item, input, token);
        if (problem is not null) return problem;
        if (input.Sequence is not > 0)
            item.Sequence = (await db.AuditPlanItems.Where(row => row.Year == item.Year && row.Kind == item.Kind)
                .MaxAsync(row => (int?)row.Sequence, token) ?? 0) + 1;
        db.AuditPlanItems.Add(item);
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.Register, "audit-plan-item", item.Id.ToString(), item.Company, "audit",
            "", Summary(item), "", token);
        return new AuditPlanResult(true, $"เพิ่ม {item.Company} ในแผน Audit {item.Year} แล้ว", Id: item.Id);
    }

    public async Task<AuditPlanResult> UpdateItemAsync(AppUser user, long id, AuditItemInput input, CancellationToken token)
    {
        var item = await db.AuditPlanItems.FirstOrDefaultAsync(row => row.Id == id, token);
        if (item is null) return Refused("ไม่พบรายการนี้ในแผน", StatusCodes.Status404NotFound);
        var before = Summary(item);
        var problem = await ApplyAsync(item, input, token);
        if (problem is not null) return problem;
        item.UpdatedBy = user.Signature;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.Update, "audit-plan-item", item.Id.ToString(), item.Company, "audit",
            before, Summary(item), "", token);
        return new AuditPlanResult(true, $"บันทึก {item.Company} แล้ว", Id: item.Id);
    }

    public async Task<AuditPlanResult> DeleteItemAsync(AppUser user, long id, CancellationToken token)
    {
        var item = await db.AuditPlanItems.FirstOrDefaultAsync(row => row.Id == id, token);
        if (item is null) return Refused("ไม่พบรายการนี้ในแผน", StatusCodes.Status404NotFound);
        db.AuditPlanItems.Remove(item);
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.Delete, "audit-plan-item", id.ToString(), item.Company, "audit",
            Summary(item), "", "นำออกจากแผน", token);
        return new AuditPlanResult(true, $"นำ {item.Company} ออกจากแผนแล้ว");
    }

    /// <summary>
    /// Rows read off the department's own sheet, pasted (1 Oct 2026). A row the plan already holds — the same
    /// company on the same date — is skipped; a row that cannot be read is reported, not guessed.
    /// </summary>
    public async Task<(int Saved, int Skipped, List<object> Errors)> ImportAsync(AppUser user, IReadOnlyList<AuditItemInput> rows,
        CancellationToken token)
    {
        var saved = 0;
        var skipped = 0;
        var errors = new List<object>();
        var existing = (await db.AuditPlanItems.AsNoTracking().Select(row => new { row.Company, row.AuditDate }).ToListAsync(token))
            .Select(row => SupplierRegister.Key(row.Company) + "|" + Formats.DateNumber(row.AuditDate)).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var key = SupplierRegister.Key(row.Company ?? "") + "|" + Formats.DateNumber(row.AuditDate ?? "");
            if (existing.Contains(key)) { skipped++; continue; }
            var result = await AddItemAsync(user, row, token);
            if (result.Ok) { saved++; existing.Add(key); }
            else errors.Add(new { row = index + 1, error = result.Message });
        }
        return (saved, skipped, errors);
    }

    /// <summary>
    /// The audit a new subcontractor is approved by, set from Add New Vendor: its open "new" line on the plan,
    /// moved to the date given, or a new line when it has none.
    /// </summary>
    public async Task<AuditPlanResult> SetVendorAuditAsync(AppUser user, int supplierId, string? date, string? person,
        string? target, CancellationToken token)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(row => row.Id == supplierId, token);
        if (supplier is null) return Refused("ไม่พบผู้ขนส่งรายนี้", StatusCodes.Status404NotFound);
        var open = await db.AuditPlanItems.Where(row => row.SupplierId == supplierId && row.Kind == AuditPlanRules.New
                && row.Status != AuditPlanRules.Done && row.Status != AuditPlanRules.Cancelled)
            .OrderByDescending(row => row.Id).FirstOrDefaultAsync(token);
        var input = new AuditItemInput(AuditPlanRules.New, supplierId, supplier.Name, target ?? open?.Target,
            person ?? open?.PersonInCharge, date, open?.Schedule ?? AuditPlanRules.Fixed, AuditPlanRules.Planned, "",
            open?.Remark, open?.FindingSentDate, open?.ReportSentDate, null);
        return open is null ? await AddItemAsync(user, input, token) : await UpdateItemAsync(user, open.Id, input, token);
    }

    public async Task<OnboardingView?> OnboardingAsync(int supplierId, CancellationToken token)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(row => row.Id == supplierId, token);
        if (supplier is null) return null;
        var rows = await db.SupplierOnboardingItems.AsNoTracking().Where(row => row.SupplierId == supplierId).ToListAsync(token);
        var kinds = VendorOnboarding.Items.Select(item => item.Kind).ToList();
        var files = await db.Documents.AsNoTracking()
            .Where(row => row.SupplierId == supplierId && kinds.Contains(row.Kind))
            .OrderByDescending(row => row.Id).ToListAsync(token);
        var items = VendorOnboarding.Items.Select(item =>
        {
            var row = rows.FirstOrDefault(one => one.Code == item.Code);
            var need = SupplierCompliance.Match(item.Kind);
            return new OnboardingItemView(item.No, item.Code, item.Document, item.Note, item.Folder, item.Kind, item.Link,
                need?.Expires ?? false, row?.Status ?? VendorOnboarding.Pending, row?.Remark ?? "", row?.UpdatedBy ?? "", row?.UpdatedAt,
                files.Where(file => file.Kind == item.Kind).Select(file => new OnboardingFileView(file.Id, file.FileName,
                    file.ExpiryDate, InlineViewing.CanShow(file.FileName), file.UploadedBy, file.UploadedAt)).ToList());
        }).ToList();
        var open = await db.AuditPlanItems.AsNoTracking().Where(row => row.SupplierId == supplierId && row.Kind == AuditPlanRules.New)
            .OrderBy(row => row.Status == AuditPlanRules.Done || row.Status == AuditPlanRules.Cancelled ? 1 : 0)
            .ThenByDescending(row => row.Id).FirstOrDefaultAsync(token);
        var auditView = open is null ? null : (await DescribeAsync([open], token)).Single();
        return new OnboardingView(supplier.Id, supplier.Name, supplier.Status,
            items.Count(item => item.Status == VendorOnboarding.Done), items.Count, items, auditView);
    }

    public async Task<AuditPlanResult> SetOnboardingAsync(AppUser user, int supplierId, string code, string? status,
        string? remark, CancellationToken token)
    {
        var item = VendorOnboarding.Find(code);
        if (item is null) return Refused("ไม่รู้จักรายการนี้", StatusCodes.Status404NotFound);
        if (!await db.Suppliers.AnyAsync(row => row.Id == supplierId, token)) return Refused("ไม่พบผู้ขนส่งรายนี้", StatusCodes.Status404NotFound);
        var wanted = (status ?? "").Trim().ToLowerInvariant();
        if (wanted.Length > 0 && !VendorOnboarding.Statuses.Contains(wanted)) return Refused("สถานะต้องเป็น Done หรือ Pending");
        var row = await db.SupplierOnboardingItems.FirstOrDefaultAsync(one => one.SupplierId == supplierId && one.Code == item.Code, token);
        var before = row is null ? "" : $"{row.Status} · {row.Remark}";
        if (row is null)
        {
            row = new SupplierOnboardingItem { SupplierId = supplierId, Code = item.Code };
            db.SupplierOnboardingItems.Add(row);
        }
        if (wanted.Length > 0) row.Status = wanted;
        if (remark is not null) row.Remark = Cut(remark.Trim(), 500);
        row.UpdatedBy = user.Signature;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.Update, "supplier-onboarding", $"{supplierId} · {item.No}", item.Document,
            item.Code, before, $"{row.Status} · {row.Remark}", "", token);
        return new AuditPlanResult(true, $"{item.No}. {(row.Status == VendorOnboarding.Done ? "Done" : "Pending")}");
    }

    /* ------------------------------------------------------------------ helpers */

    private static AuditPlanResult Refused(string message, int status = StatusCodes.Status400BadRequest) => new(false, message, status);

    private static string Text(string? value) => (value ?? "").Trim();

    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;

    private static string Summary(AuditPlanItem item) =>
        $"{item.Kind} · {item.Company} · {item.Target} · {item.AuditDate} · {item.Schedule} · {item.Status}"
        + (item.NextDate.Length > 0 ? $" → {item.NextDate}" : "");

    private static AuditPlanHeaderView Header(AuditPlan? plan, int year) => plan is null
        ? new(year, DefaultTitle(year), "", "", "", "", "", "00", "", null)
        : new(plan.Year, plan.Title, plan.PreparedBy, plan.ReviewedBy, plan.ReviewedDate, plan.SecondReviewedBy,
            plan.ApprovedBy, plan.Revision, plan.UpdatedBy, plan.UpdatedAt);

    /// <summary>Every field from the form onto the row, or why not. The company links to the register when it is there.</summary>
    private async Task<AuditPlanResult?> ApplyAsync(AuditPlanItem item, AuditItemInput input, CancellationToken token)
    {
        var kind = Text(input.Kind).ToLowerInvariant();
        if (kind.Length == 0) kind = item.Kind;
        if (!AuditPlanRules.Kinds.Contains(kind)) return Refused("ประเภทต้องเป็น Re-audit หรือ New");
        var date = Text(input.AuditDate);
        if (Formats.DateNumber(date) == 0) return Refused("ต้องระบุวันที่ Audit เป็นรูปแบบ DD/MM/YYYY");
        var schedule = Text(input.Schedule).ToLowerInvariant();
        if (schedule.Length == 0) schedule = AuditPlanRules.Fixed;
        if (!AuditPlanRules.Schedules.Contains(schedule)) return Refused("กำหนดการต้องเป็น Fixed หรือ Tentative");
        var status = Text(input.Status).ToLowerInvariant();
        if (status.Length == 0) status = AuditPlanRules.Planned;
        if (!AuditPlanRules.Statuses.Contains(status)) return Refused("สถานะไม่ถูกต้อง");
        var next = Text(input.NextDate);
        if (next.Length > 0 && Formats.DateNumber(next) == 0) return Refused("วันที่เลื่อนไป / ทำต่อ ต้องเป็นรูปแบบ DD/MM/YYYY");
        if (status is AuditPlanRules.Postponed or AuditPlanRules.Continue && next.Length == 0)
            return Refused(status == AuditPlanRules.Postponed ? "ต้องระบุวันที่เลื่อนไป" : "ต้องระบุวันที่ทำต่อ");
        var finding = Text(input.FindingSentDate);
        var report = Text(input.ReportSentDate);
        if ((finding.Length > 0 && Formats.DateNumber(finding) == 0) || (report.Length > 0 && Formats.DateNumber(report) == 0))
            return Refused("วันที่ส่ง Finding / Report ต้องเป็นรูปแบบ DD/MM/YYYY");

        // The company: the supplier picked, or the name typed matched to the register — any status, since the
        // audit is what approves a new one. A company the register does not know yet is kept as written.
        int? supplierId = null;
        var company = Text(input.Company);
        if (input.SupplierId is { } wanted)
        {
            var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(row => row.Id == wanted, token);
            if (supplier is null) return Refused("ไม่พบผู้ขนส่งรายนี้ในทะเบียน");
            supplierId = supplier.Id;
            company = supplier.Name;
        }
        else if (company.Length > 0 && (await names.AnyAsync(token))(company) is { } match)
        {
            supplierId = match.Id;
            company = match.Name;
        }
        if (company.Length == 0) return Refused("ต้องระบุบริษัท");

        item.Kind = kind;
        item.Year = AuditPlanRules.YearOf(date);
        item.SupplierId = supplierId;
        item.Company = Cut(company, 240);
        item.Target = Cut(Text(input.Target).ToUpperInvariant(), 60);
        item.PersonInCharge = Cut(string.Join('\n', (input.PersonInCharge ?? "").Split('\n')
            .Select(line => line.Trim()).Where(line => line.Length > 0)), 400);
        item.AuditDate = date;
        item.Schedule = schedule;
        item.Status = status;
        item.NextDate = status is AuditPlanRules.Postponed or AuditPlanRules.Continue ? next : "";
        item.Remark = Cut(Text(input.Remark), 500);
        item.FindingSentDate = finding;
        item.ReportSentDate = report;
        if (input.Sequence is > 0 and var sequence) item.Sequence = sequence;
        return null;
    }

    private async Task<List<AuditPlanItemView>> DescribeAsync(IReadOnlyList<AuditPlanItem> items, CancellationToken token)
    {
        var ids = items.Where(row => row.SupplierId != null).Select(row => row.SupplierId!.Value).Distinct().ToList();
        var statuses = await db.Suppliers.AsNoTracking().Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.Status, token);
        var today = Today();
        return items.Select(row => new AuditPlanItemView(row.Id, row.Year, row.Kind, row.Sequence, row.SupplierId, row.Company,
            row.SupplierId is { } id ? statuses.GetValueOrDefault(id, "") : "", row.Target, row.PersonInCharge, row.AuditDate,
            row.Schedule, row.Status, AuditPlanRules.NotYetDone(row.Status, row.AuditDate, today), row.NextDate, row.Remark,
            row.FindingSentDate, row.ReportSentDate, row.UpdatedBy, row.UpdatedAt)).ToList();
    }
}
