using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Offered">Carriers asked to accept a job the register now names them on.</param>
/// <param name="Withdrawn">Asks closed because the register came to name another carrier, or none.</param>
/// <param name="Billed">Billing Cases opened for jobs closed COMPLETED.</param>
public record RegisterFollowResult(int Offered, int Withdrawn, int Billed);

/// <summary>
/// What the register's carrier and status mean for the carrier (30 Sep 2026, asked for by the
/// department lead).
///
/// <para>
/// A job keyed in SCMOS with a carrier on it goes to that carrier's NEW job to accept before it is in its
/// My job: naming a registered carrier on an open job is asking it, the same pending ask the workflow's
/// "ขอรถ" writes. When the register comes to name another carrier, or none, the open ask is closed and the
/// carrier it was addressed to no longer sees the job. A spelling of the same company asks nobody again.
/// Jobs keyed before this, which the carrier already holds by name, are left as they are.
/// </para>
///
/// <para>
/// And back in SCMOS: a job asked about and not yet that far reads WAITING_SUPPLIER in the department's
/// register; the carrier's yes moves it to SUPPLIER_CONFIRMED (<see cref="CarrierService.AcceptAssignmentAsync"/>);
/// its no raises the bell's "ผู้ขนส่งไม่รับงาน" until another carrier is named. The job's owner may accept for
/// the carrier (<see cref="AcceptForCarrierAsync"/>); changing the status in the grid does not.
/// </para>
///
/// <para>
/// A job closed COMPLETED opens the carrier's Billing Case, whoever closed it — the department's grid, the
/// carrier's own, LINE or the TMS. <see cref="SweepAsync"/> catches every road to COMPLETED and, on its
/// first runs, every job closed before this (the department's decision: all of them, due by the real
/// delivery date).
/// </para>
/// </summary>
public class RegisterCarrierFollower(ScmosDbContext db, JobsRepository jobs, CarrierService carriers,
    CarrierBillingService billing, CarrierWebhookQueue webhooks, AuditService audit, ILogger<RegisterCarrierFollower> log)
{
    private static readonly TimeSpan Thailand = TimeSpan.FromHours(7);

    private List<(int Id, string Name, string Code)>? _carriers;
    private List<(int SupplierId, string Alias)>? _aliases;

    /// <summary>After a save of the register: asks, withdrawals and Billing Cases for what it changed. Never throws for one job.</summary>
    public async Task<RegisterFollowResult> FollowAsync(IReadOnlyList<RegisterChange> changes, AppUser user, CancellationToken token)
    {
        int offered = 0, withdrawn = 0, billed = 0;
        var waiting = new List<string>();
        foreach (var change in changes)
        {
            try
            {
                var after = JobStatus.Canonical(change.StatusAfter);
                var carrierMoved = change.Created ? change.TruckerAfter.Trim().Length > 0
                    : !string.Equals(change.TruckerBefore.Trim(), change.TruckerAfter.Trim(), StringComparison.OrdinalIgnoreCase);
                // A closed job is not offered: it is billed below, or it was cancelled.
                if (carrierMoved && !JobStatus.IsClosedOut(after))
                {
                    var (asked, closed) = await AskAsync(change, user, token);
                    offered += asked;
                    withdrawn += closed;
                    if (asked > 0) waiting.Add(change.Key);
                }
                if (after == JobStatus.Completed && (change.Created || JobStatus.Canonical(change.StatusBefore) != JobStatus.Completed)
                    && await BillAsync(change.Key, user, token))
                    billed++;
            }
            catch (Exception problem) when (problem is not OperationCanceledException)
            {
                // The save has landed; what follows it is retried by the sweep (billing) or by the next save.
                db.ChangeTracker.Clear();
                log.LogError(problem, "Following the register failed for job {Key}", change.Key);
            }
        }
        try
        {
            await WaitForCarrierAsync(waiting, user, token);
        }
        catch (Exception problem) when (problem is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            log.LogError(problem, "Marking {Count} asked job(s) WAITING_SUPPLIER failed", waiting.Count);
        }
        return new(offered, withdrawn, billed);
    }

    /// <summary>
    /// Every COMPLETED job with a registered carrier and no Billing Case, a page at a time, each given its
    /// case. Returns how many were opened. A job another carrier holds an open ask on is left, and asked
    /// about again next time.
    /// </summary>
    public async Task<int> SweepAsync(AppUser actor, CancellationToken token, int page = 200)
    {
        var completed = JobStatus.CompletedSpellings;
        var truckers = await db.OperationJobs.AsNoTracking()
            .Where(job => completed.Contains(job.Status) && job.Trucker != "" && !db.BillingCases.Any(row => row.JobKey == job.Key))
            .Select(job => job.Trucker).Distinct().ToListAsync(token);
        var billable = new List<string>();
        foreach (var name in truckers)
            if (await ResolveAsync(name, token) is not null) billable.Add(name);
        if (billable.Count == 0) return 0;

        var opened = 0;
        var after = "";
        while (!token.IsCancellationRequested)
        {
            var keys = await db.OperationJobs.AsNoTracking()
                .Where(job => completed.Contains(job.Status) && billable.Contains(job.Trucker)
                    && string.Compare(job.Key, after) > 0 && !db.BillingCases.Any(row => row.JobKey == job.Key))
                .OrderBy(job => job.Key).Select(job => job.Key).Take(page).ToListAsync(token);
            if (keys.Count == 0) break;
            foreach (var key in keys)
            {
                try
                {
                    if (await BillAsync(key, actor, token)) opened++;
                }
                catch (Exception problem) when (problem is not OperationCanceledException)
                {
                    db.ChangeTracker.Clear();
                    log.LogError(problem, "Opening the Billing Case failed for job {Key}", key);
                }
            }
            after = keys[^1];
        }
        return opened;
    }

    /// <summary>The ask on a job still waiting for its carrier's answer, or null.</summary>
    public Task<SupplierRequest?> PendingAskAsync(string key, CancellationToken token) =>
        db.SupplierRequests.AsNoTracking().Where(row => row.JobKey == key && row.Outcome == CarrierAssignment.Pending)
            .OrderByDescending(row => row.Id).FirstOrDefaultAsync(token);

    /// <summary>
    /// The job's owner accepts the waiting ask for its carrier (30 Sep 2026: the carrier said yes outside SCMOS).
    /// The carrier's own acceptance does the work — the job goes to the carrier's My job and reads
    /// SUPPLIER_CONFIRMED in SCMOS unless it is already further on — and the ask is marked as the owner's,
    /// <see cref="CarrierAssignment.OwnerAccepted"/>. Who may do it is the caller's check.
    /// </summary>
    public async Task<(bool Ok, string Message, int Status)> AcceptForCarrierAsync(AppUser user, string key, CancellationToken token)
    {
        var ask = await PendingAskAsync(key, token);
        if (ask is null) return (false, "งานนี้ไม่มีคำขอที่รอผู้ขนส่งกดรับ", StatusCodes.Status409Conflict);
        var supplierId = ask.SupplierId ?? await ResolveAsync(ask.Carrier, token);
        var company = supplierId is null ? null
            : await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(row => row.Id == supplierId, token);
        if (company is null) return (false, $"ไม่พบ {ask.Carrier} ใน Supplier Register", StatusCodes.Status409Conflict);

        var statusBefore = await db.OperationJobs.AsNoTracking().Where(row => row.Key == key)
            .Select(row => new { row.Status, row.JobCode }).FirstOrDefaultAsync(token);
        var accepted = await carriers.AcceptAssignmentAsync(company, key, ask.Id, "", "", "", "", "", user.Signature, token);
        if (!accepted.Ok) return (false, accepted.Message, StatusCodes.Status409Conflict);

        var row = await db.SupplierRequests.FirstAsync(one => one.Id == ask.Id, token);
        row.ReasonCode = CarrierAssignment.OwnerAccepted;
        row.Reason = $"เจ้าของงานรับงานแทน {ask.Carrier}";
        audit.Stage(user, AuditActions.Update, "carrier-assignment", ask.Id.ToString(), key,
            "outcome", CarrierAssignment.Pending, CarrierAssignment.Confirmed, row.Reason);
        if (accepted.Written?.GetValueOrDefault("status") is { } status && statusBefore is not null
            && !string.Equals(status, statusBefore.Status, StringComparison.OrdinalIgnoreCase))
            audit.Stage(user, AuditActions.StatusChange, "job", key, statusBefore.JobCode.Length > 0 ? statusBefore.JobCode : key,
                AuditActions.For("status")?.Label ?? "status", statusBefore.Status, status, row.Reason);
        await db.SaveChangesAsync(token);
        return (true, $"รับงานแทน {ask.Carrier} แล้ว — งานอยู่ใน My job ของผู้ขนส่ง", StatusCodes.Status200OK);
    }

    /// <summary>The carrier named on a job, as the Supplier Register knows it, or null.</summary>
    public async Task<int?> ResolveAsync(string trucker, CancellationToken token)
    {
        if (trucker.Trim().Length == 0) return null;
        _carriers ??= (await db.Suppliers.AsNoTracking().Where(row => row.IsCarrier)
            .Select(row => new { row.Id, row.Name, row.Code }).ToListAsync(token))
            .Select(row => (row.Id, row.Name, row.Code)).ToList();
        _aliases ??= (await db.SupplierAliases.AsNoTracking().Select(row => new { row.SupplierId, row.Alias }).ToListAsync(token))
            .Select(row => (row.SupplierId, row.Alias)).ToList();
        return WorkflowService.ResolveSupplier(trucker, _carriers, _aliases);
    }

    /// <summary>
    /// The register named a new carrier on an open job, or took it off: the open ask of any other carrier
    /// is closed, and a registered carrier not yet asked is asked.
    /// </summary>
    private async Task<(int Offered, int Withdrawn)> AskAsync(RegisterChange change, AppUser user, CancellationToken token)
    {
        var named = change.TruckerAfter.Trim();
        var wanted = await ResolveAsync(named, token);
        // The same company under another spelling: nobody new is asked, nobody loses the job.
        if (!change.Created && wanted is not null && wanted == await ResolveAsync(change.TruckerBefore, token)) return (0, 0);

        var history = await db.SupplierRequests.Where(row => row.JobKey == change.Key).ToListAsync(token);
        var active = history.Where(row => CarrierAssignment.IsActive(row.Outcome)).ToList();
        var held = new List<SupplierRequest>();
        foreach (var row in active)
            if (wanted is not null && (row.SupplierId ?? await ResolveAsync(row.Carrier, token)) == wanted) held.Add(row);
        if (held.Count > 0) return (0, 0);

        // Only one ask is open on a job at a time (the unique index): the old one closes, and is saved, first.
        var reason = named.Length > 0 ? $"ผู้ขนส่งในตารางงานเปลี่ยนเป็น {named}" : "ลบชื่อผู้ขนส่งออกจากตารางงาน";
        foreach (var row in active)
        {
            audit.Stage(user, AuditActions.Update, "carrier-assignment", row.Id.ToString(), change.Key,
                "outcome", row.Outcome, CarrierAssignment.Superseded, reason);
            row.Outcome = CarrierAssignment.Superseded;
            row.ReasonCode = CarrierAssignment.RegisterChanged;
            row.Reason = reason;
            row.Remark = reason;
            row.RespondedBy = user.Signature;
        }
        if (active.Count > 0) await db.SaveChangesAsync(token);
        foreach (var row in active)
            await webhooks.CancelledAsync(change.Key, row.Carrier, row.Id, reason, "", token);
        if (wanted is null) return (0, active.Count);

        var rank = history.Count == 0 ? 1 : history.Max(row => row.Rank) + 1;
        var ask = new SupplierRequest
        {
            JobKey = change.Key, SupplierId = wanted, Rank = rank, Carrier = named,
            Outcome = CarrierAssignment.Pending, RequestedBy = user.Signature, RequestedAt = DateTimeOffset.UtcNow,
        };
        db.SupplierRequests.Add(ask);
        audit.Stage(user, AuditActions.Update, "carrier-assignment", change.Key, change.Key,
            "outcome", "", CarrierAssignment.Pending, $"#{rank} {named} · ผู้ขนส่งในตารางงาน รอผู้ขนส่งยืนยันรับงาน");
        await db.SaveChangesAsync(token);
        // The carrier's own system hears of the ask, when it asked to — as the workflow's ask does.
        await webhooks.OfferedAsync(change.Key, named, ask.Id, null, ask.RequestedAt, "", token);
        return (1, active.Count);
    }

    /// <summary>
    /// The jobs just asked about that are not yet as far as waiting for their carrier now are: WAITING_SUPPLIER,
    /// in one write, each audited. Later statuses stay.
    /// </summary>
    private async Task WaitForCarrierAsync(IReadOnlyList<string> keys, AppUser user, CancellationToken token)
    {
        var rows = new List<OperationJob>();
        foreach (var chunk in keys.Chunk(1000))
            rows.AddRange(await db.OperationJobs.AsNoTracking().Where(row => chunk.Contains(row.Key)).ToListAsync(token));
        var moved = new List<(OperationJob Job, JsonElement Row)>();
        foreach (var job in rows)
        {
            var ladder = JobStatus.For(job.Cat);
            var at = Array.IndexOf(ladder, JobStatus.Canonical(job.Status));
            if (at < 0 || at >= Array.IndexOf(ladder, JobStatus.WaitingSupplier)) continue;
            if (System.Text.Json.Nodes.JsonNode.Parse(job.Data) is not System.Text.Json.Nodes.JsonObject node) continue;
            node["status"] = JobStatus.WaitingSupplier;
            node["key"] = job.Key;
            moved.Add((job, JsonSerializer.SerializeToElement(node)));
        }
        if (moved.Count == 0) return;
        await jobs.SaveAsync(moved.Select(one => one.Row).ToList(), user.Signature, token);
        foreach (var (job, _) in moved)
            audit.Stage(user, AuditActions.StatusChange, "job", job.Key, job.JobCode.Length > 0 ? job.JobCode : job.Key,
                AuditActions.For("status")?.Label ?? "status", job.Status, JobStatus.WaitingSupplier, "ส่งงานให้ผู้ขนส่งยืนยันรับงาน");
        await db.SaveChangesAsync(token);
    }

    /// <summary>The Billing Case of a COMPLETED job, for the carrier holding it. False when none was opened.</summary>
    private async Task<bool> BillAsync(string key, AppUser actor, CancellationToken token)
    {
        if (await db.BillingCases.AnyAsync(row => row.JobKey == key, token)) return false;
        var job = await db.OperationJobs.FirstOrDefaultAsync(row => row.Key == key, token);
        if (job is null || JobStatus.Canonical(job.Status) != JobStatus.Completed) return false;

        // The carrier that confirmed it, or else the one the register names.
        var confirmed = await db.SupplierRequests.AsNoTracking()
            .Where(row => row.JobKey == key && row.Outcome == CarrierAssignment.Confirmed)
            .OrderByDescending(row => row.Id).FirstOrDefaultAsync(token);
        var holder = confirmed is null ? await ResolveAsync(job.Trucker, token)
            : confirmed.SupplierId ?? await ResolveAsync(confirmed.Carrier, token);
        if (holder is null) return false;
        var supplier = await db.Suppliers.FirstOrDefaultAsync(row => row.Id == holder, token);
        if (supplier is null) return false;

        var (_, created) = await billing.EnsureForDeliveryAsync(actor, supplier, job, DeliveredAt(job), token);
        if (created) await db.SaveChangesAsync(token);
        return created;
    }

    /// <summary>
    /// When the job was really delivered, for the billing due date: its arrival date and time (Thai time),
    /// else its planned date and time, else when it was last written. Never later than now.
    /// </summary>
    public static DateTimeOffset DeliveredAt(OperationJob job)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            using var document = JsonDocument.Parse(job.Data);
            string Field(string name) => document.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            foreach (var (day, time) in new[] { ("arrDate", "arrTime"), ("date", "planTime") })
            {
                if (Formats.ParseDay(Field(day)) is not { } on) continue;
                var minutes = Formats.TimeMinutes(Field(time)) ?? 0;
                var at = new DateTimeOffset(on.ToDateTime(TimeOnly.MinValue), Thailand).AddMinutes(minutes);
                return at > now ? now : at;
            }
        }
        catch (JsonException) { /* an unreadable row falls back to when it was written */ }
        return job.UpdatedAt > now ? now : job.UpdatedAt;
    }
}

/// <summary>
/// The Billing Case sweep: every <c>Billing:SweepMinutes</c> (30 by default, 0 turns it off), and first a
/// few minutes after start, so the jobs closed before 30 Sep 2026 are billed without anyone asking.
/// </summary>
public class BillingCaseSweep(IServiceProvider services, IConfiguration configuration, ILogger<BillingCaseSweep> log) : BackgroundService
{
    public const string EveryKey = "Billing:SweepMinutes";
    public const int DefaultMinutes = 30;

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        var minutes = configuration.GetValue(EveryKey, DefaultMinutes);
        if (minutes <= 0) return;
        try { await Task.Delay(TimeSpan.FromMinutes(4), stopping); }
        catch (OperationCanceledException) { return; }

        var by = new AppUser("scheduler", "", "SCMOS", "System", "", "system", Recognised: true);
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var opened = await scope.ServiceProvider.GetRequiredService<RegisterCarrierFollower>().SweepAsync(by, stopping);
                if (opened > 0) log.LogInformation("Billing sweep: {Opened} Billing Case(s) opened for COMPLETED jobs", opened);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
            catch (Exception problem)
            {
                log.LogError(problem, "Billing sweep failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(minutes), stopping); }
            catch (OperationCanceledException) { return; }
        }
    }
}
