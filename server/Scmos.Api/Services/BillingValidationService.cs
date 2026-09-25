using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record BillingValidationView(int Sequence, string Step, string Code, string Category,
    bool Blocking, string Message, decimal? ExpectedAmount, decimal? ActualAmount,
    string Currency, string EvidenceType, string EvidenceId, string EvidenceVersion,
    string RuleSource, string EffectiveDate);

public record BillingSubmitResult(bool Ok, string Code, string Message, string Status,
    IReadOnlyList<BillingValidationView> Results);

/// <summary>Deterministic Phase 5 carrier-submit validation. Every decision is persisted with its evidence.</summary>
public class BillingValidationService(ScmosDbContext db, AuditService audit)
{
    public async Task<BillingSubmitResult> SubmitAsync(AppUser actor, int supplierId, long invoiceId,
        CancellationToken token)
    {
        var invoice = await db.BillingInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId && x.SupplierId == supplierId, token);
        if (invoice is null) return Fail("NOT_FOUND", "ไม่พบใบวางบิลนี้");
        if (invoice.Status != BillingInvoiceStatus.Draft && invoice.Status != BillingInvoiceStatus.Blocked)
            return Fail("NOT_SUBMITTABLE", "ส่งตรวจได้เฉพาะ Draft หรือรายการที่ Validation ไม่ผ่าน");
        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber) || invoice.InvoiceDate is null)
            return Fail("INVOICE_INCOMPLETE", "กรุณาระบุเลขที่และวันที่ใบแจ้งหนี้");

        var link = await db.BillingInvoiceJobLinks.AsNoTracking().FirstOrDefaultAsync(x => x.InvoiceId == invoiceId, token);
        if (link is null) return Fail("JOB_NOT_LINKED", "ใบวางบิลยังไม่ได้เชื่อมกับงาน");
        var billingCase = await db.BillingCases.FirstAsync(x => x.Id == link.BillingCaseId, token);
        var job = await db.OperationJobs.AsNoTracking().FirstAsync(x => x.Key == billingCase.JobKey, token);
        var supplier = await db.Suppliers.AsNoTracking().FirstAsync(x => x.Id == supplierId, token);
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var workDate = ParseDate(job.WorkDate) ?? invoice.InvoiceDate.Value;
        var facts = JobFacts.Read(job);
        var output = new List<BillingValidationResult>();
        void Add(string step, BillingRuleResult value, string evidenceType = "", string evidenceId = "",
            string version = "", string source = "", DateOnly? effective = null)
        {
            output.Add(new BillingValidationResult { InvoiceId = invoice.Id, Sequence = output.Count + 1,
                Step = step, Code = value.Code, Category = value.Category, Blocking = value.Blocking,
                Message = value.Message, ExpectedAmount = value.Expected, ActualAmount = value.Actual,
                Currency = invoice.Currency, EvidenceType = evidenceType, EvidenceId = evidenceId,
                EvidenceVersion = version, RuleSource = source, EffectiveDate = effective, CreatedAt = now });
        }

        var assignmentOutcome = await db.SupplierRequests.AsNoTracking().Where(x => x.Id == billingCase.AssignmentId)
            .Select(x => x.Outcome).FirstOrDefaultAsync(token) ?? "";
        Add("ELIGIBILITY", BillingEligibility.IsEligible(job.Status, assignmentOutcome)
            ? Pass("JOB_ELIGIBLE", "Job is Delivery Complete")
            : Block("JOB_NOT_ELIGIBLE", "Job is not eligible for billing"));
        Add("OWNERSHIP", billingCase.SupplierId == supplierId
            ? Pass("CARRIER_OWNERSHIP_MATCH", "Billing Case belongs to the signed-in carrier")
            : Block("CARRIER_OWNERSHIP_MISMATCH", "Billing Case belongs to another carrier"));

        var sameNumber = await db.BillingInvoices.AsNoTracking().AnyAsync(x => x.Id != invoice.Id
            && x.SupplierId == supplierId && x.InvoiceNumber == invoice.InvoiceNumber, token);
        var alreadyBilled = await (from otherLink in db.BillingInvoiceJobLinks.AsNoTracking()
            join other in db.BillingInvoices.AsNoTracking() on otherLink.InvoiceId equals other.Id
            where otherLink.BillingCaseId == billingCase.Id && other.Id != invoice.Id
                && (other.Status == "PAID" || other.Status == "FINANCE_COMPLETE" || other.Status == "CLOSED")
            select other.Id).AnyAsync(token);
        var duplicateRisk = await db.BillingInvoices.AsNoTracking().AnyAsync(x => x.Id != invoice.Id
            && x.SupplierId == supplierId && x.InvoiceDate == invoice.InvoiceDate && x.TotalAmount == invoice.TotalAmount, token);
        Add("DUPLICATE", BillingValidationRules.Duplicate(sameNumber, alreadyBilled, duplicateRisk));

        var documents = await db.Documents.AsNoTracking().Where(x => x.BillingInvoiceId == invoice.Id).ToListAsync(token);
        var rules = await db.BillingRequirementRules.AsNoTracking().Where(x => x.Active
            && x.EffectiveFrom <= workDate && (x.EffectiveTo == null || x.EffectiveTo >= workDate)
            && (x.SupplierId == null || x.SupplierId == supplierId)).ToListAsync(token);
        rules = rules.Where(x => Match(x.Customer, job.Customer) && Match(x.ServiceType, facts.Service)
            && Match(x.ShipmentType, job.Cat)).OrderByDescending(x => x.Priority).ThenBy(x => x.Id).ToList();
        var previousSnapshots = await db.BillingRequirementSnapshots.Where(x => x.InvoiceId == invoice.Id).ToListAsync(token);
        if (previousSnapshots.Count == 0)
        {
            foreach (var rule in rules)
            {
                var document = documents.FirstOrDefault(x => Equal(x.Kind, rule.DocumentKind) || Equal(x.Folder, rule.DocumentKind));
                db.BillingRequirementSnapshots.Add(new BillingRequirementSnapshot { InvoiceId = invoice.Id,
                    RuleId = rule.Id, RuleCode = rule.Code, DocumentKind = rule.DocumentKind, Required = rule.Required,
                    Blocking = rule.Blocking, Priority = rule.Priority, Satisfied = document is not null,
                    DocumentId = document?.Id, SnapshottedAt = now });
                if (rule.Required) Add("DOCUMENT", BillingValidationRules.Document(document is not null, rule.Blocking),
                    "BillingRequirementRule", rule.Id.ToString(), rule.Code, rule.SpecificRequirement, rule.EffectiveFrom);
            }
        }
        else
        {
            foreach (var snap in previousSnapshots.Where(x => x.Required).OrderByDescending(x => x.Priority))
            {
                var document = documents.FirstOrDefault(x => Equal(x.Kind, snap.DocumentKind) || Equal(x.Folder, snap.DocumentKind));
                snap.Satisfied = document is not null; snap.DocumentId = document?.Id;
                Add("DOCUMENT", BillingValidationRules.Document(document is not null, snap.Blocking),
                    "BillingRequirementSnapshot", snap.Id.ToString(), snap.RuleCode, "snapshot", null);
            }
        }

        var charges = await db.BillingAdditionalCharges.AsNoTracking().Where(x => x.InvoiceId == invoice.Id).ToListAsync(token);
        foreach (var charge in charges)
            Add("ADDITIONAL_CHARGE", BillingValidationRules.Charge(charge.Status is "APPROVED" or "PARTIALLY_APPROVED"
                && charge.ApprovedAmount is not null), "BillingAdditionalCharge", charge.Id.ToString(), charge.Status, charge.ChargeType);
        var approvedCharges = charges.Where(x => x.Status is "APPROVED" or "PARTIALLY_APPROVED").Sum(x => x.ApprovedAmount ?? 0m);

        var rate = await ResolveRateAsync(supplier, job, facts, workDate, token);
        var baseClaim = invoice.Subtotal - approvedCharges;
        var rateRule = BillingValidationRules.Rate(rate.Matches, rate.Expected, baseClaim);
        Add("CONTRACT_RATE", rateRule, "RatePrice", rate.PriceId?.ToString() ?? "", rate.Version,
            rate.Source, workDate);

        var taxRules = await db.BillingTaxRules.AsNoTracking().Where(x => x.Active
            && x.EffectiveFrom <= workDate && (x.EffectiveTo == null || x.EffectiveTo >= workDate)
            && (x.SupplierId == null || x.SupplierId == supplierId)).ToListAsync(token);
        taxRules = taxRules.Where(x => Match(x.Customer, job.Customer) && Match(x.ServiceType, facts.Service)
            && Match(x.ShipmentType, job.Cat)).OrderByDescending(x => x.Priority).ThenByDescending(x => x.EffectiveFrom).ToList();
        if (taxRules.Count > 0) taxRules = taxRules.Where(x => x.Priority == taxRules[0].Priority).ToList();
        BillingTaxRule? tax = taxRules.Count == 1 ? taxRules[0] : null;
        var expectedTax = tax is null ? (decimal?)null : decimal.Round(invoice.Subtotal * tax.Rate, 2, MidpointRounding.AwayFromZero);
        Add("TAX", taxRules.Count > 1
                ? Block("MULTIPLE_TAX_RULE_MATCH", "Multiple effective tax rules have the same applicability")
                : BillingValidationRules.Tax(expectedTax, invoice.TaxAmount),
            "BillingTaxRule", tax?.Id.ToString() ?? "", tax?.Code ?? "", tax?.TaxType ?? "", tax?.EffectiveFrom);

        var expectedTotal = invoice.Subtotal + invoice.TaxAmount;
        Add("RECONCILIATION", expectedTotal == invoice.TotalAmount
            ? Pass("INVOICE_TOTAL_MATCH", "Invoice total reconciles") with { Expected = expectedTotal, Actual = invoice.TotalAmount }
            : Block("INVOICE_TOTAL_MISMATCH", "Invoice total does not reconcile") with { Expected = expectedTotal, Actual = invoice.TotalAmount });

        var runNo = (await db.BillingValidationRuns.Where(x => x.InvoiceId == invoice.Id).MaxAsync(x => (int?)x.Sequence, token) ?? 0) + 1;
        var blocked = output.Any(x => x.Blocking);
        var outcome = blocked ? BillingValidationCategory.Blocked
            : output.Any(x => x.Category == BillingValidationCategory.Exception) ? BillingValidationCategory.Exception
            : output.Any(x => x.Category == BillingValidationCategory.Warning) ? BillingValidationCategory.Warning
            : BillingValidationCategory.Pass;
        var run = new BillingValidationRun { InvoiceId = invoice.Id, Sequence = runNo, Outcome = outcome,
            SubmittedBy = actor.Signature, StartedAt = now, CompletedAt = DateTimeOffset.UtcNow };
        db.BillingValidationRuns.Add(run);
        await db.SaveChangesAsync(token);
        foreach (var result in output) { result.RunId = run.Id; db.BillingValidationResults.Add(result); }
        invoice.SubmittedAt ??= now; invoice.ValidatedAt = DateTimeOffset.UtcNow;
        invoice.Status = blocked ? BillingInvoiceStatus.Blocked : BillingInvoiceStatus.Validated;
        invoice.UpdatedBy = actor.Signature; invoice.UpdatedAt = now;
        billingCase.Status = blocked ? BillingCaseStatus.Blocked : BillingCaseStatus.Validated;
        billingCase.UpdatedBy = actor.Signature; billingCase.UpdatedAt = now;
        audit.Stage(actor, AuditActions.Update, "billing-invoice", invoice.Id.ToString(), invoice.InvoiceNumber,
            "status", BillingInvoiceStatus.Draft, invoice.Status, $"Phase 5 validation: {outcome}");
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new(true, "OK", blocked ? "ตรวจพบรายการที่ต้องแก้ไขก่อนส่งต่อ" : "Validation ผ่านและบันทึกผลแล้ว",
            invoice.Status, output.Select(Describe).ToList());
    }

    public async Task<IReadOnlyList<BillingValidationView>> LatestAsync(long invoiceId, CancellationToken token)
    {
        var run = await db.BillingValidationRuns.AsNoTracking().Where(x => x.InvoiceId == invoiceId)
            .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(token);
        if (run is null) return [];
        var rows = await db.BillingValidationResults.AsNoTracking().Where(x => x.RunId == run.Id)
            .OrderBy(x => x.Sequence).ToListAsync(token);
        return rows.Select(Describe).ToList();
    }

    private async Task<RateResolution> ResolveRateAsync(Supplier supplier, OperationJob job, JobFacts facts,
        DateOnly effective, CancellationToken token)
    {
        if (facts.Vehicle.Length == 0 || facts.Diesel is null) return new(0, null, null, "", "", "");
        var bands = await db.FuelBands.AsNoTracking().OrderBy(x => x.Position).ToListAsync(token);
        var band = RateService.BandFor(bands.Select(x => new BandView(x.Label, x.MinPrice, x.MaxPrice, x.Position)).ToList(), facts.Diesel.Value);
        if (band < 0) return new(0, null, null, "", "", "");
        var lanes = await db.RateLanes.AsNoTracking().Where(x => x.SupplierId == supplier.Id || x.SupplierId == null).ToListAsync(token);
        lanes = lanes.Where(x => (x.SupplierId == supplier.Id || AnyCarrier(x.Carrier, supplier.Name))
            && Match(x.Customer, job.Customer) && Match(x.Service, facts.Service)
            && PlaceMatch(x.ToPlace, facts.Destination)).ToList();
        var ids = lanes.Select(x => x.Id).ToList();
        var prices = await db.RatePrices.AsNoTracking().Where(x => ids.Contains(x.LaneId)
            && x.BandPosition == band).ToListAsync(token);
        prices = prices.Where(x => Equal(x.Vehicle, facts.Vehicle)).ToList();
        if (prices.Count != 1) return new(prices.Count, null, null, "", "", "");
        var price = prices[0]; var lane = lanes.Single(x => x.Id == price.LaneId);
        return new(1, price.Price, price.Id, lane.SourceFile,
            lane.PromotedAt?.ToString("O", CultureInfo.InvariantCulture) ?? lane.SourceFile,
            $"rate_lanes/{lane.Id}; band={band}; service={lane.Service}");
    }

    private static BillingValidationView Describe(BillingValidationResult x) => new(x.Sequence, x.Step, x.Code,
        x.Category, x.Blocking, x.Message, x.ExpectedAmount, x.ActualAmount, x.Currency, x.EvidenceType,
        x.EvidenceId, x.EvidenceVersion, x.RuleSource, x.EffectiveDate?.ToString("yyyy-MM-dd") ?? "");
    private static BillingSubmitResult Fail(string code, string message) => new(false, code, message, "", []);
    private static BillingRuleResult Pass(string code, string message) => new(code, BillingValidationCategory.Pass, false, message);
    private static BillingRuleResult Block(string code, string message) => new(code, BillingValidationCategory.Blocked, true, message);
    private static bool Match(string rule, string value) => string.IsNullOrWhiteSpace(rule) || Equal(rule, value);
    private static bool Equal(string? a, string? b) => Key(a) == Key(b);
    private static string Key(string? value) => new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static bool PlaceMatch(string rule, string value) => string.IsNullOrWhiteSpace(rule) || Key(rule) == Key(value);
    private static bool AnyCarrier(string list, string name) => list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => Equal(x, name));
    private static DateOnly? ParseDate(string value) => DateOnly.TryParseExact(value, ["dd/MM/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    private record RateResolution(int Matches, decimal? Expected, long? PriceId, string Version, string Source, string Detail);
    private record JobFacts(string Destination, string Vehicle, string Service, decimal? Diesel)
    {
        public static JobFacts Read(OperationJob job)
        {
            try { using var json = JsonDocument.Parse(job.Data); var root = json.RootElement;
                string Get(string name) => root.TryGetProperty(name, out var v) ? v.ToString().Trim() : "";
                decimal? diesel = decimal.TryParse(Get("diesel"), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
                return new(Get("destination").Length > 0 ? Get("destination") : Get("plant"), Get("type"), Get("fclLcl"), diesel);
            } catch { return new("", "", "", null); }
        }
    }
}
