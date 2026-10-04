using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// Annual Carrier Evaluation, Phase 13 (2 Oct 2026): the spec's critical flow end to end, with its audit trail; writes
/// that land whole or not at all when the database fails part-way; two submissions of one sheet at the same moment;
/// failures logged, never with a token; and who reaches what, read from the services themselves.
/// </summary>
static class EvaluationHardeningChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        if (sql) await SqlAsync(check);
    }

    /// <summary>Every log line, formatted, with its exception — what an operator would read.</summary>
    private sealed class Captured<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines) Lines.Add((level, formatter(state, exception) + (exception is null ? "" : " | " + exception)));
        }
    }

    /// <summary>The database failing at the given save — a lost connection part-way through a write.</summary>
    private sealed class FailOnSave(int at) : SaveChangesInterceptor
    {
        private int _saves;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result,
            CancellationToken token = default) =>
            ++_saves == at ? throw new InvalidOperationException("simulated: the database went away") : base.SavingChangesAsync(data, result, token);
    }

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_EVALUATION_HARDENING_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        DbContextOptions<ScmosDbContext> Options(params IInterceptor[] interceptors) => new DbContextOptionsBuilder<ScmosDbContext>()
            .UseSqlServer(connection, s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).AddInterceptors(interceptors).Options;
        await using var db = new ScmosDbContext(Options());
        await db.Database.EnsureCreatedAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var alpha = new Supplier { Name = "ALPHA TRANSPORT", Code = "ALP", Status = "approved", IsCarrier = true, CreatedAt = now, UpdatedAt = now };
            var bravo = new Supplier { Name = "BRAVO LOGISTICS", Code = "BRV", Status = "approved", IsCarrier = true, CreatedAt = now, UpdatedAt = now };
            db.Suppliers.AddRange(alpha, bravo);
            await db.SaveChangesAsync();
            OperationJob Job(string key, string trucker, string date) => new()
            {
                Key = key, Trucker = trucker, WorkDate = date, Status = JobStatus.Completed, UpdatedAt = now,
                Data = JsonSerializer.Serialize(new { key, date, status = JobStatus.Completed, trucker, planTime = "08:00", arrDate = date, arrTime = "07:50" }),
            };
            db.OperationJobs.AddRange(Job("H1", "ALPHA TRANSPORT", "10/03/2026"), Job("H2", "BRAVO LOGISTICS", "11/03/2026"));
            await db.SaveChangesAsync();

            AppUser User(string id, string role) => new(id.ToLowerInvariant(), id.ToLowerInvariant() + "@test.invalid", id, role, id, "test", true);
            var operation = User("OP-1", Roles.Operation);
            var supervisor = User("SV-1", Roles.Supervisor);
            var assistant = User("AM-1", Roles.AssistantManager);
            var carrier = User("SUB-1", Roles.Subcontractor);
            var snapshotLog = new Captured<EvaluationSnapshotService>();
            var scoringLog = new Captured<EvaluationScoringService>();
            var externalLog = new Captured<ExternalEvaluationService>();

            // The services over a context, so the same code runs against a database that fails part-way.
            AuditService Auditing(ScmosDbContext over) => new(over, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
            CarrierDirectory Directory(ScmosDbContext over) => new(over, new MemoryCache(new MemoryCacheOptions()));
            EvaluationSnapshotService Snapshots(ScmosDbContext over) => new(over, Auditing(over),
                new JobRegisterCache(over, new MemoryCache(new MemoryCacheOptions()), NullLogger<JobRegisterCache>.Instance), Directory(over), snapshotLog);
            EvaluationScoringService Scoring(ScmosDbContext over) => new(over, Auditing(over), scoringLog);
            ExternalEvaluationService External(ScmosDbContext over) => new(over, Auditing(over), externalLog);
            var campaigns = new AnnualEvaluationService(db, Auditing(db), Directory(db));
            var invitations = new EvaluationInvitationService(db, Auditing(db));
            var review = new EvaluationReviewService(db, Auditing(db));
            var legacy = new LegacyEvaluationService(db, Auditing(db));

            // Admin creates the campaign and selects its carriers.
            var id = (int)(await campaigns.CreateAsync(supervisor, new CreateCampaignInput(2026, null, null), default)).Id!.Value;
            await campaigns.ChangeCarriersAsync(supervisor, id, new CarrierChangeInput("add-eligible", null, null), default);
            var alphaRow = await db.EvaluationCarriers.AsNoTracking().SingleAsync(row => row.CampaignId == id && row.SupplierId == alpha.Id);
            var bravoRow = await db.EvaluationCarriers.AsNoTracking().SingleAsync(row => row.CampaignId == id && row.SupplierId == bravo.Id);
            await campaigns.MoveAsync(supervisor, id, AnnualEvaluationRules.DataPreparation, null, default);
            var code = (await db.EvaluationCampaigns.AsNoTracking().SingleAsync(row => row.Id == id)).Code;

            // The snapshot: the database fails at BRAVO's version, after ALPHA's was saved inside the same transaction.
            await using (var failing = new ScmosDbContext(Options(new FailOnSave(3))))
            {
                var lost = await Snapshots(failing).GenerateAsync(supervisor, id, null, null, default);
                check(!lost.Ok && lost.Status == StatusCodes.Status500InternalServerError && !await db.EvaluationSnapshots.AnyAsync()
                    && (await db.EvaluationCarriers.AsNoTracking().SingleAsync(row => row.Id == alphaRow.Id)).TotalJobs == alphaRow.TotalJobs
                    && snapshotLog.Lines.Any(line => line.Level == LogLevel.Error && line.Text.Contains(code) && line.Text.Contains("nothing written")),
                    "hardening: a snapshot that fails part-way writes nothing — not even the carrier saved before the failure — and the failure is logged");
            }
            var taken = await Snapshots(db).GenerateAsync(supervisor, id, null, null, default);
            var versions = await db.EvaluationSnapshots.AsNoTracking().ToListAsync();
            check(taken.Ok && versions.Count == 2 && versions.All(one => one.Version == 1 && one.Current)
                && snapshotLog.Lines.Any(line => line.Level == LogLevel.Information && line.Text.Contains("2 carrier(s)") && line.Text.Contains(" ms")),
                "hardening: the same snapshot then succeeds as version 1 for both — nothing was left behind; its time is logged");

            // Assigns an evaluator; the links are generated. Open for answers today (the opening rules are Phase 2's).
            var ops = await db.EvaluationDepartments.SingleAsync(row => row.Code == "ops");
            await invitations.AddEvaluatorAsync(supervisor, id, new EvaluatorInput("Ops reviewer", "ops@test.invalid", ops.Id), default);
            var evaluatorId = (await db.EvaluationEvaluators.SingleAsync(row => row.CampaignId == id)).Id;
            var links = (await invitations.GenerateAsync(supervisor, id, new InvitationRequest([evaluatorId], null, null), default)).Links!;
            var alphaLink = links.Single(link => link.Carrier == "ALPHA TRANSPORT");
            var bravoLink = links.Single(link => link.Carrier == "BRAVO LOGISTICS");
            var campaign = await db.EvaluationCampaigns.SingleAsync(row => row.Id == id);
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
            campaign.Status = AnnualEvaluationRules.Open;
            campaign.OpenOn = today.AddDays(-1);
            campaign.DueOn = today.AddDays(5);
            campaign.MinimumSystemCoverage = 0;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            // The evaluator opens the link and sees the evidence.
            var sheet = (await External(db).ReadAsync(alphaLink.Token, default)).View!;
            var answers = sheet.Questions.Select(question => new EvaluationInvitations.Answer(question.Code, 4, false, "")).ToList();
            check(sheet.Carrier == "ALPHA TRANSPORT" && sheet.Performance.Count > 0 && sheet.Questions.Count > 0,
                "flow: the evaluator opens the link and sees their carrier's figures and questions");

            // Submits — and the database fails between the response and its answers.
            await using (var failing = new ScmosDbContext(Options(new FailOnSave(2))))
            {
                var lost = await External(failing).SubmitAsync(alphaLink.Token, new ExternalSubmission(answers, ""), default);
                var link = await db.EvaluationInvitations.AsNoTracking().SingleAsync(row => row.EvaluatorId == evaluatorId && row.EvaluationCarrierId == alphaRow.Id);
                check(lost.Status == StatusCodes.Status500InternalServerError && link.Status != AnnualEvaluationRules.InvitationSubmitted
                    && !await db.EvaluationResponses.AnyAsync() && !await db.EvaluationAnswers.AnyAsync(),
                    "hardening: a sheet whose answers fail to save leaves no response and the link still open — the evaluator can send it again");
            }
            var sent = await External(db).SubmitAsync(alphaLink.Token, new ExternalSubmission(answers, ""), default);
            check(sent.Status == StatusCodes.Status200OK && await db.EvaluationAnswers.CountAsync() == answers.Count,
                "flow: the evaluator sends it again and every answer is kept");

            // BRAVO's sheet sent twice at the same moment, from two requests.
            await using (var first = new ScmosDbContext(Options()))
            await using (var second = new ScmosDbContext(Options()))
            {
                var bravoSheet = (await External(first).ReadAsync(bravoLink.Token, default)).View!;
                var bravoAnswers = bravoSheet.Questions.Select(question => new EvaluationInvitations.Answer(question.Code, 3, false, "")).ToList();
                var both = await Task.WhenAll(External(first).SubmitAsync(bravoLink.Token, new ExternalSubmission(bravoAnswers, ""), default),
                    External(second).SubmitAsync(bravoLink.Token, new ExternalSubmission(bravoAnswers, ""), default));
                check(both.Select(one => one.Status).Order().SequenceEqual([StatusCodes.Status200OK, StatusCodes.Status409Conflict])
                    && await db.EvaluationResponses.CountAsync(row => row.EvaluationCarrierId == bravoRow.Id) == 1
                    && await db.EvaluationAnswers.CountAsync() == answers.Count + bravoAnswers.Count,
                    "hardening: one sheet sent twice at the same moment is kept once; the other request is told it was already sent");
            }

            // Calculation: the database fails at BRAVO's result, after ALPHA's was saved inside the same transaction.
            await using (var failing = new ScmosDbContext(Options(new FailOnSave(3))))
            {
                var lost = await Scoring(failing).CalculateAsync(supervisor, id, null, null, default);
                check(!lost.Ok && lost.Status == StatusCodes.Status500InternalServerError && !await db.EvaluationResults.AnyAsync()
                    && !await db.EvaluationDepartmentScores.AnyAsync()
                    && scoringLog.Lines.Any(line => line.Level == LogLevel.Error && line.Text.Contains(code)),
                    "hardening: a calculation that fails part-way writes no result for any carrier, and the failure is logged");
            }
            var scored = await Scoring(db).CalculateAsync(supervisor, id, null, null, default);
            var results = await db.EvaluationResults.AsNoTracking().ToListAsync();
            check(scored.Ok && results.Count == 2 && results.All(one => one.Version == 1 && one.Current && one.HumanScore != null),
                "flow: SCMOS aggregates the department answers and calculates — version 1 for both, nothing left from the failure");

            // Management reviews: the user's rules of 4 Oct 2026 — a supervisor records the decisions, an Assistant Manager or
            // above approves and finalizes, and a decision to stop giving a carrier work never changes its status by itself.
            await campaigns.MoveAsync(supervisor, id, AnnualEvaluationRules.Closed, null, default);
            await campaigns.MoveAsync(supervisor, id, AnnualEvaluationRules.UnderReview, null, default);
            var recorded = await review.DecideAsync(supervisor, id, alphaRow.Id, EvaluationReview.Continue, "", default);
            var suspended = await review.DecideAsync(supervisor, id, bravoRow.Id, EvaluationReview.SuspendNewAllocation, "Ratings of 3 across the sheet", default);
            var bySupervisor = await campaigns.MoveAsync(supervisor, id, AnnualEvaluationRules.Approved, null, default);
            var approved = await campaigns.MoveAsync(assistant, id, AnnualEvaluationRules.Approved, null, default);
            var finalized = await campaigns.MoveAsync(assistant, id, AnnualEvaluationRules.Finalized, null, default);
            var published = await db.SupplierEvaluations.AsNoTracking().Where(row => row.Period == code).ToListAsync();
            check(recorded.Ok && suspended.Ok && bySupervisor.Status == StatusCodes.Status403Forbidden && approved.Ok && finalized.Ok
                && published.Count == 2 && published.All(row => row.Source == SupplierEvaluation.CampaignSource)
                && (await db.Suppliers.AsNoTracking().SingleAsync(row => row.Id == bravo.Id)).Status == "approved",
                "flow: a supervisor records each decision but cannot approve; an Assistant Manager approves and finalizes into the Supplier Register — a carrier suspended from new work keeps its register status");

            // §35: each step of the flow is in the audit trail, the evaluator's own steps marked as coming from outside.
            var trail = await db.AuditEvents.AsNoTracking().Where(row => row.Entity == "annual-evaluation").ToListAsync();
            string[] fields = ["campaign", "carriers", "snapshot", "evaluator", "invitations", "score", $"decision:{alphaRow.Id}", $"decision:{bravoRow.Id}", "published"];
            check(fields.All(field => trail.Any(row => row.Field == field))
                && new[] { AnnualEvaluationRules.DataPreparation, AnnualEvaluationRules.Closed, AnnualEvaluationRules.UnderReview, AnnualEvaluationRules.Approved,
                    AnnualEvaluationRules.Finalized }.All(state => trail.Any(row => row.Field == "status" && row.NewValue == state))
                && trail.Count(row => row.Field.StartsWith("invitation:") && row.Source == "external" && row.NewValue == AnnualEvaluationRules.InvitationSubmitted) == 2
                && trail.Count(row => row.Field == "snapshot") == 1 && trail.Count(row => row.Field == "score") == 1,
                "audit: created, carriers, every move, the snapshot, evaluator and links, each opening and sending, the calculation, each decision and the publishing — a failed write records nothing");

            // Refusals after the fact, then: no log line holds a token or its hash.
            var unknown = await External(db).SubmitAsync(EvaluationInvitations.NewToken(), new ExternalSubmission(answers, ""), default);
            var again = await External(db).SubmitAsync(alphaLink.Token, new ExternalSubmission(answers, ""), default);
            var secrets = links.SelectMany(link => new[] { link.Token, EvaluationInvitations.HashOf(link.Token) }).ToList();
            var everything = snapshotLog.Lines.Concat(scoringLog.Lines).Concat(externalLog.Lines).ToList();
            check(unknown.Status == StatusCodes.Status404NotFound && again.Status != StatusCodes.Status200OK
                && externalLog.Lines.Count(line => line.Level == LogLevel.Warning && line.Text.Contains("refused")) >= 3
                && externalLog.Lines.Any(line => line.Text.Contains("refused (404)") && line.Text.Contains("unknown"))
                && externalLog.Lines.Any(line => line.Level == LogLevel.Error && line.Text.Contains("link stays open"))
                && everything.All(line => secrets.All(secret => !line.Text.Contains(secret, StringComparison.OrdinalIgnoreCase))),
                "observability: refused and failed submissions are logged by the link's row id — no token and no token hash in any line");

            // Who reaches what, asked of the services: Operation reads, a supervisor runs, an Assistant Manager or above approves, a carrier account reaches nothing.
            var fresh = (int)(await campaigns.CreateAsync(supervisor, new CreateCampaignInput(2027, null, null), default)).Id!.Value;
            check((await Snapshots(db).GenerateAsync(operation, fresh, null, null, default)).Status == StatusCodes.Status403Forbidden
                && (await Scoring(db).CalculateAsync(operation, fresh, null, null, default)).Status == StatusCodes.Status403Forbidden
                && (await invitations.AddEvaluatorAsync(operation, fresh, new EvaluatorInput("X", "x@test.invalid", ops.Id), default)).Status == StatusCodes.Status403Forbidden
                && (await review.DecideAsync(operation, id, alphaRow.Id, EvaluationReview.Continue, "", default)).Status == StatusCodes.Status403Forbidden
                && (await campaigns.MoveAsync(supervisor, fresh, AnnualEvaluationRules.Approved, null, default)).Status == StatusCodes.Status403Forbidden
                && await Scoring(db).ResultsAsync(operation, id, default) is not null && await legacy.HistoryAsync(operation, default) is not null
                && await Scoring(db).ResultsAsync(carrier, id, default) is null && await review.SummaryAsync(carrier, id, default) is null
                && await Snapshots(db).ReadAsync(carrier, id, alphaRow.Id, null, default) is null && await legacy.HistoryAsync(carrier, default) is null
                && (await Snapshots(db).GenerateAsync(carrier, fresh, null, null, default)).Status == StatusCodes.Status403Forbidden
                && (await campaigns.CreateAsync(carrier, new CreateCampaignInput(2028, null, null), default)).Status == StatusCodes.Status403Forbidden,
                "authorization: Operation reads and runs nothing; a supervisor runs but does not approve; a carrier account reaches none of it");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
