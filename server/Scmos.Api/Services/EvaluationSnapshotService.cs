using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record SnapshotMetricView(string Code, string Status, decimal? Value, decimal? Numerator, decimal? Denominator, string Formula,
    string Note, IReadOnlyList<string> Sources, bool External);
public record SnapshotVersionView(long Id, int Version, bool Current, string Reason, string GeneratedBy, DateTimeOffset GeneratedAt);
public record SnapshotView(int EvaluationCarrierId, int SupplierId, string Carrier, SnapshotVersionView? Snapshot,
    IReadOnlyList<SnapshotMetricView> Metrics, IReadOnlyList<SnapshotVersionView> Versions);

/// <summary>
/// The evidence each carrier of a campaign is scored on (1 Oct 2026, Annual Evaluation Phase 3). Taking a snapshot
/// reads the period's jobs, delays, issues, invoices, PODs and the carrier's required documents once, works the
/// figures out with <see cref="EvaluationEvidence"/>, and writes them as a new version. A version is never edited:
/// taking another moves <see cref="EvaluationSnapshot.Current"/> and the old one stays, so what an evaluator was shown
/// can always be shown again. Before the campaign opens that is free; after, it needs a reason, and an approved
/// campaign takes no new evidence at all.
/// </summary>
public class EvaluationSnapshotService(ScmosDbContext db, AuditService audit, JobRegisterCache register, CarrierDirectory carriers)
{
    public async Task<AnnualEvaluationResult> GenerateAsync(AppUser user, int campaignId, IReadOnlyList<int>? only, string? reason,
        CancellationToken token)
    {
        if (!user.Can(Capability.ManageAnnualEvaluation)) return Refused("บัญชีนี้ไม่มีสิทธิ์สร้าง snapshot", StatusCodes.Status403Forbidden);
        var campaign = await db.EvaluationCampaigns.FirstOrDefaultAsync(row => row.Id == campaignId, token);
        if (campaign is null) return Refused("ไม่พบแคมเปญนี้", StatusCodes.Status404NotFound);
        if (campaign.Status is AnnualEvaluationRules.Approved or AnnualEvaluationRules.Finalized or AnnualEvaluationRules.Archived)
            return Refused("แคมเปญอนุมัติแล้ว — หลักฐานไม่เปลี่ยนอีก", StatusCodes.Status409Conflict);
        var why = (reason ?? "").Trim();
        var locked = AnnualEvaluationRules.Locked(campaign.Status);
        if (locked && why.Length < 4) return Refused("แคมเปญเปิดแล้ว — การสร้าง snapshot ใหม่ต้องระบุเหตุผล (อย่างน้อย 4 ตัวอักษร)");

        var rows = await db.EvaluationCarriers.Where(row => row.CampaignId == campaignId && row.Included).ToListAsync(token);
        if (only is { Count: > 0 }) rows = rows.Where(row => only.Contains(row.Id)).ToList();
        if (rows.Count == 0) return Refused("ไม่มีผู้ขนส่งที่จะสร้าง snapshot");

        var evidence = await ReadEvidenceAsync(campaign, rows.Select(row => row.SupplierId).ToHashSet(), token);
        var now = DateTimeOffset.UtcNow;
        var versions = await db.EvaluationSnapshots.Where(row => rows.Select(one => one.Id).Contains(row.EvaluationCarrierId))
            .ToListAsync(token);
        foreach (var row in rows)
        {
            var metrics = EvaluationEvidence.Build(evidence(row.SupplierId));
            foreach (var old in versions.Where(one => one.EvaluationCarrierId == row.Id && one.Current)) old.Current = false;
            var snapshot = new EvaluationSnapshot
            {
                CampaignId = campaignId, EvaluationCarrierId = row.Id,
                Version = versions.Where(one => one.EvaluationCarrierId == row.Id).Select(one => one.Version).DefaultIfEmpty(0).Max() + 1,
                Current = true, Reason = why.Length > 500 ? why[..500] : why, GeneratedBy = user.Signature, GeneratedAt = now,
            };
            db.EvaluationSnapshots.Add(snapshot);
            await db.SaveChangesAsync(token);
            db.EvaluationSnapshotMetrics.AddRange(metrics.Select(metric => new EvaluationSnapshotMetric
            {
                SnapshotId = snapshot.Id, Code = metric.Code, Status = metric.Status, Value = metric.Value, Numerator = metric.Numerator,
                Denominator = metric.Denominator, Formula = metric.Formula, Note = metric.Note.Length > 1000 ? metric.Note[..1000] : metric.Note,
                Sources = JsonSerializer.Serialize(metric.Sources), External = metric.External,
            }));
            // Before the campaign opens, the carrier's own counts follow the evidence; after, they stay as they were.
            if (!locked)
            {
                row.TotalJobs = (int)(metrics.First(metric => metric.Code == "total-jobs").Value ?? 0);
                row.CompletedJobs = (int)(metrics.First(metric => metric.Code == "completed-jobs").Value ?? 0);
                row.Eligibility = AnnualEvaluationRules.Eligibility(row.CompletedJobs.Value, campaign.MinimumJobs);
                row.CountedAt = now;
            }
            await db.SaveChangesAsync(token);
        }
        await audit.RecordAsync(user, AuditActions.Register, "annual-evaluation", campaignId.ToString(CultureInfo.InvariantCulture),
            campaign.Code, "snapshot", "", $"{rows.Count} ราย", why, token);
        return new AnnualEvaluationResult(true, $"สร้าง snapshot {rows.Count} ราย", Id: campaignId);
    }

    public async Task<SnapshotView?> ReadAsync(AppUser user, int campaignId, int evaluationCarrierId, int? version, CancellationToken token)
    {
        if (!user.Can(Capability.ViewAnnualEvaluation)) return null;
        var row = await db.EvaluationCarriers.AsNoTracking().FirstOrDefaultAsync(one => one.Id == evaluationCarrierId && one.CampaignId == campaignId, token);
        if (row is null) return null;
        var supplier = await db.Suppliers.AsNoTracking().Where(one => one.Id == row.SupplierId)
            .Select(one => one.LegalName != "" ? one.LegalName : one.Name).FirstOrDefaultAsync(token) ?? "";
        var versions = (await db.EvaluationSnapshots.AsNoTracking().Where(one => one.EvaluationCarrierId == row.Id).OrderByDescending(one => one.Version)
            .ToListAsync(token)).Select(one => new SnapshotVersionView(one.Id, one.Version, one.Current, one.Reason, one.GeneratedBy, one.GeneratedAt)).ToList();
        var chosen = version is { } wanted ? versions.FirstOrDefault(one => one.Version == wanted) : versions.FirstOrDefault(one => one.Current);
        var metrics = chosen is null ? [] : (await db.EvaluationSnapshotMetrics.AsNoTracking().Where(one => one.SnapshotId == chosen.Id)
                .OrderBy(one => one.Id).ToListAsync(token))
            .Select(one => new SnapshotMetricView(one.Code, one.Status, one.Value, one.Numerator, one.Denominator, one.Formula, one.Note,
                JsonSerializer.Deserialize<List<string>>(one.Sources) ?? [], one.External)).ToList();
        return new SnapshotView(row.Id, row.SupplierId, supplier, chosen, metrics, versions);
    }

    /// <summary>
    /// Reads the period once for every carrier asked for, and answers each carrier's inputs from it. The register is the
    /// one snapshot every screen reads; which company a spelling means is the carrier directory's.
    /// </summary>
    private async Task<Func<int, EvaluationEvidence.Inputs>> ReadEvidenceAsync(EvaluationCampaign campaign, HashSet<int> suppliers,
        CancellationToken token)
    {
        var from = Number(campaign.PeriodStart);
        var to = Number(campaign.PeriodEnd);
        var snapshot = await register.ReadAsync(token);
        var directory = await carriers.ReadAsync(token);
        var idOf = (await db.Suppliers.AsNoTracking().Select(row => new { row.Id, row.Name }).ToListAsync(token))
            .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Min(row => row.Id), StringComparer.OrdinalIgnoreCase);

        // Every job of the period, whoever carried it: an issue is laid at a carrier through its job, and the job of
        // somebody not being evaluated still says the issue is not this carrier's.
        var periodJobs = snapshot.Rows
            .Where(row => row.Record is not null && Formats.DateNumber(row.Record.Date) is var day && day >= from && day <= to
                && !WorkspaceTabs.IsCancelled(row.Record.Status))
            .Select(row => (row.Key, Carrier: directory.Company(row.Trucker), Record: row.Record!)).ToList();
        int? SupplierOf(string company) => idOf.TryGetValue(company, out var id) ? id : null;
        var jobsOf = periodJobs.Where(job => SupplierOf(job.Carrier) is { } id && suppliers.Contains(id))
            .ToLookup(job => SupplierOf(job.Carrier)!.Value);
        var keys = jobsOf.SelectMany(group => group.Select(job => job.Key)).ToHashSet(StringComparer.Ordinal);

        var delays = (await db.DelayRecords.AsNoTracking().Select(row => new { row.JobKey, row.AgainstCarrier }).ToListAsync(token))
            .Where(row => keys.Contains(row.JobKey)).ToLookup(row => row.JobKey);

        var owner = CarrierScorecard.OwnerOf(periodJobs, spelling => directory.Knows(spelling) ? directory.Company(spelling) : null);
        var issuesOf = (await db.OperationalIssues.AsNoTracking().ToListAsync(token))
            .Where(issue => Formats.DateNumber(issue.FoundOn) is var day && day >= from && day <= to)
            .Select(issue => (Issue: issue, Supplier: SupplierOf(owner(issue))))
            .Where(pair => pair.Supplier is { } id && suppliers.Contains(id))
            .ToLookup(pair => pair.Supplier!.Value, pair => pair.Issue);
        var caseIds = issuesOf.SelectMany(group => group).Where(issue => issue.CaseId is not null).Select(issue => issue.CaseId!.Value).Distinct().ToList();
        var caseStages = caseIds.Count == 0 ? new Dictionary<long, string>()
            : await db.IncidentCases.AsNoTracking().Where(row => caseIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Stage, token);

        // Invoices dated by when the carrier submitted them; billing cases by when the job was delivered.
        var start = Start(campaign.PeriodStart);
        var end = Start(campaign.PeriodEnd.AddDays(1));
        var supplierIds = suppliers.ToList();
        var invoicesOf = (await db.Set<BillingInvoice>().AsNoTracking()
                .Where(row => supplierIds.Contains(row.SupplierId) && row.SubmittedAt != null && row.SubmittedAt >= start && row.SubmittedAt < end)
                .Select(row => new { row.SupplierId, row.InvoiceNumber, row.Id, row.ReviewCycle, row.OnlineApprovedAt }).ToListAsync(token))
            .ToLookup(row => row.SupplierId, row => new EvaluationEvidence.Invoice(row.InvoiceNumber.Length > 0 ? row.InvoiceNumber : "#" + row.Id,
                row.ReviewCycle, row.OnlineApprovedAt));
        var cases = await db.Set<BillingCase>().AsNoTracking()
            .Where(row => supplierIds.Contains(row.SupplierId) && row.DeliveryCompletedAt >= start && row.DeliveryCompletedAt < end)
            .Select(row => new { row.Id, row.SupplierId, row.JobKey, row.DeliveryCompletedAt, row.CreatedAt, row.SlaDueDate }).ToListAsync(token);
        var billingCaseIds = cases.Select(row => row.Id).ToList();
        var firstSubmission = billingCaseIds.Count == 0 ? new Dictionary<long, DateTimeOffset?>()
            : (await (from link in db.Set<BillingInvoiceJobLink>().AsNoTracking()
                      join invoice in db.Set<BillingInvoice>().AsNoTracking() on link.InvoiceId equals invoice.Id
                      where billingCaseIds.Contains(link.BillingCaseId) && invoice.SubmittedAt != null
                      select new { link.BillingCaseId, invoice.SubmittedAt }).ToListAsync(token))
                .GroupBy(row => row.BillingCaseId).ToDictionary(group => group.Key, group => group.Min(row => row.SubmittedAt));
        var slaOf = cases.ToLookup(row => row.SupplierId, row => new EvaluationEvidence.SlaCase(row.JobKey, row.DeliveryCompletedAt, row.CreatedAt,
            row.SlaDueDate, firstSubmission.GetValueOrDefault(row.Id)));

        var withPod = (await db.Documents.AsNoTracking().Where(row => row.Folder == "POD" && row.JobKey != "").Select(row => row.JobKey).ToListAsync(token))
            .Where(keys.Contains).ToHashSet(StringComparer.Ordinal);

        var today = SupplierCompliance.Today();
        var supplierDocuments = await db.Documents.AsNoTracking().Where(row => row.SupplierId != null && supplierIds.Contains(row.SupplierId!.Value))
            .Select(row => new { row.Id, row.SupplierId, row.Kind, row.ExpiryDate }).ToListAsync(token);
        var current = SupplierCompliance.CurrentSupplierDocuments(supplierDocuments, row => row.SupplierId, row => row.Kind, row => row.ExpiryDate,
            row => row.Id);
        IReadOnlyList<(string, string)> ComplianceOf(int supplier) => SupplierCompliance.Required.Select(need =>
        {
            var held = current.FirstOrDefault(row => row.SupplierId == supplier && SupplierCompliance.Match(row.Kind)?.Code == need.Code);
            return (need.Code, SupplierCompliance.StateOf(held is not null, held?.ExpiryDate ?? "", today,
                need.Expires && (held is null || SupplierCompliance.MonitorsExpiry(need, held.ExpiryDate))));
        }).ToList();

        var bangkokToday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        return supplier =>
        {
            var jobs = jobsOf[supplier].Select(job => new EvaluationEvidence.Job(job.Key, job.Record)).ToList();
            var jobKeys = jobs.Select(job => job.Key).ToHashSet(StringComparer.Ordinal);
            return new EvaluationEvidence.Inputs(jobs,
                jobKeys.SelectMany(key => delays[key]).Select(row => new EvaluationEvidence.Delay(row.JobKey, row.AgainstCarrier)).ToList(),
                issuesOf[supplier].ToList(), caseStages, invoicesOf[supplier].ToList(), slaOf[supplier].ToList(),
                withPod.Where(jobKeys.Contains).ToHashSet(StringComparer.Ordinal), ComplianceOf(supplier), campaign.MinimumJobs, bangkokToday);
        };
    }

    private static DateTimeOffset Start(DateOnly day) => new(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.FromHours(7));

    private static int Number(DateOnly day) => day.Year * 10000 + day.Month * 100 + day.Day;

    private static AnnualEvaluationResult Refused(string message, int status = StatusCodes.Status400BadRequest) => new(false, message, status);
}
