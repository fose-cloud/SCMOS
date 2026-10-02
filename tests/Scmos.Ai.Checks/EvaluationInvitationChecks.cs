using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// Annual Carrier Evaluation, Phases 6–7 (1 Oct 2026): the links evaluators outside SCMOS answer through. A token is
/// shown once and stored only as its hash; it opens one carrier's sheet, which is answered once; revoked, expired and
/// not-yet-open links are refused; the page is sent only the figures it may show.
/// </summary>
static class EvaluationInvitationChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        var token = EvaluationInvitations.NewToken();
        var other = EvaluationInvitations.NewToken();
        var hash = EvaluationInvitations.HashOf(token);
        check(token.Length == 43 && token != other && EvaluationInvitations.LooksLikeToken(token)
            && !EvaluationInvitations.LooksLikeToken("abc") && !EvaluationInvitations.LooksLikeToken(token + "A")
            && !EvaluationInvitations.LooksLikeToken(token[..42] + ".") && !EvaluationInvitations.LooksLikeToken(null)
            && hash.Length == 64 && hash == hash.ToLowerInvariant() && hash.All(Uri.IsHexDigit) && hash == EvaluationInvitations.HashOf(token)
            && hash != EvaluationInvitations.HashOf(other) && !hash.Contains(token),
            "evaluation links: a token is 256 random bits in 43 URL-safe characters, kept only as its SHA-256");
        check(EvaluationInvitations.PartitionOf(token) == EvaluationInvitations.PartitionOf(token)
            && EvaluationInvitations.PartitionOf(token) != EvaluationInvitations.PartitionOf(other)
            && EvaluationInvitations.PartitionOf(token).StartsWith("eval:") && !EvaluationInvitations.PartitionOf(token).Contains(token[..10])
            && EvaluationInvitations.PartitionOf("garbage") == "eval:anonymous" && EvaluationInvitations.PartitionOf(null) == "eval:anonymous"
            && EvaluationInvitations.AllowanceOf("eval:anonymous") < EvaluationInvitations.AllowanceOf(EvaluationInvitations.PartitionOf(token)),
            "evaluation links: requests are limited per link, not per address — every evaluator comes through the same proxy — and malformed ones share one small bucket");
        var now = DateTimeOffset.UtcNow;
        check(EvaluationInvitations.StateOf(AnnualEvaluationRules.InvitationPending, now.AddMinutes(-1), now) == EvaluationInvitations.Expired
            && EvaluationInvitations.StateOf(AnnualEvaluationRules.InvitationOpened, now.AddMinutes(-1), now) == EvaluationInvitations.Expired
            && EvaluationInvitations.StateOf(AnnualEvaluationRules.InvitationSubmitted, now.AddMinutes(-1), now) == AnnualEvaluationRules.InvitationSubmitted
            && EvaluationInvitations.StateOf(AnnualEvaluationRules.InvitationRevoked, now.AddDays(1), now) == AnnualEvaluationRules.InvitationRevoked
            && EvaluationInvitations.StateOf(AnnualEvaluationRules.InvitationSent, now.AddDays(1), now) == AnnualEvaluationRules.InvitationSent,
            "evaluation links: an unanswered link past its day is expired; an answered or revoked one stays what it was");

        List<EvaluationInvitations.Asked> asked = [new("quality", true, 2), new("service", false, 2)];
        EvaluationInvitations.Answer Rate(string code, int? rating, string comment = "", bool na = false) => new(code, rating, na, comment);
        int Count(params EvaluationInvitations.Answer[] answers) => EvaluationInvitations.Problems(asked, answers, "").Count;
        check(Count() == 1 && Count(Rate("quality", null, na: true)) == 0 && Count(Rate("quality", 3)) == 0
            && Count(Rate("quality", 2)) == 1 && Count(Rate("quality", 2, "late twice in May")) == 0
            && Count(Rate("quality", 4), Rate("service", 1)) == 1 && Count(Rate("quality", 4), Rate("service", null)) == 0
            && Count(Rate("quality", 6)) == 1 && Count(Rate("quality", 0, "x")) == 1 && Count(Rate("quality", 4, na: true)) == 1
            && Count(Rate("quality", 4), Rate("pricing", 4)) == 1 && Count(Rate("quality", 4), Rate("quality", 5)) == 1
            && Count(Rate("quality", 4, new string('x', EvaluationInvitations.MaxComment + 1))) == 1
            && EvaluationInvitations.Problems(asked, [Rate("quality", 4)], new string('x', EvaluationInvitations.MaxComment * 2 + 1)).Count == 1,
            "evaluation sheet: N/A answers a question without scoring it; a low rating needs a reason; ratings are 1–5; unknown or repeated questions are refused");
        check(ExternalEvaluationService.Shown.All(code => EvaluationEvidence.KpiMetric.Values.Contains(code) || code is "total-jobs" or "completed-jobs"
                or "incidents-major" or "incidents-minor" or "claims" or "billing-sla")
            && !ExternalEvaluationService.Shown.Contains("pricing") && !ExternalEvaluationService.Shown.Contains("late-unclassified"),
            "evaluation page: an evaluator sees counts and rates only — no pricing, no internal classification");

        if (sql) await SqlAsync(check);
    }

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_EVALUATION_LINKS_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var db = new ScmosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var alpha = new Supplier { Name = "ALPHA TRANSPORT", Code = "ALP", Status = "approved", IsCarrier = true, CreatedAt = now, UpdatedAt = now };
            db.Suppliers.Add(alpha);
            await db.SaveChangesAsync();
            OperationJob Job(string key, string date) => new()
            {
                Key = key, Trucker = "ALPHA TRANSPORT", WorkDate = date, Status = JobStatus.Completed, UpdatedAt = now,
                Data = JsonSerializer.Serialize(new { key, date, status = JobStatus.Completed, trucker = "ALPHA TRANSPORT", planTime = "08:00", arrDate = date, arrTime = "07:50" }),
            };
            db.OperationJobs.AddRange(Job("SECRET-JOB-1", "10/03/2026"), Job("SECRET-JOB-2", "11/03/2026"));
            await db.SaveChangesAsync();

            AppUser User(string id, string role) => new(id.ToLowerInvariant(), id.ToLowerInvariant() + "@test.invalid", id, role, id, "test", true);
            var operation = User("OP-1", Roles.Operation);
            var supervisor = User("SV-1", Roles.Supervisor);
            var auditing = new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
            var directory = new CarrierDirectory(db, new MemoryCache(new MemoryCacheOptions()));
            var campaigns = new AnnualEvaluationService(db, auditing, directory);
            var snapshots = new EvaluationSnapshotService(db, auditing,
                new JobRegisterCache(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<JobRegisterCache>.Instance), directory);
            var invitations = new EvaluationInvitationService(db, auditing);
            var external = new ExternalEvaluationService(db, auditing);
            var scoring = new EvaluationScoringService(db, auditing);

            var id = (int)(await campaigns.CreateAsync(supervisor, new CreateCampaignInput(2026, null, null), default)).Id!.Value;
            await campaigns.ChangeCarriersAsync(supervisor, id, new CarrierChangeInput("add-eligible", null, null), default);
            var carrierId = (await db.EvaluationCarriers.SingleAsync(row => row.CampaignId == id)).Id;
            var ops = await db.EvaluationDepartments.SingleAsync(row => row.Code == "ops");
            var cs = await db.EvaluationDepartments.SingleAsync(row => row.Code == "cs");

            var addByOperation = await invitations.AddEvaluatorAsync(operation, id, new EvaluatorInput("Ops reviewer", "ops@test.invalid", ops.Id), default);
            var addOps = await invitations.AddEvaluatorAsync(supervisor, id, new EvaluatorInput("Ops reviewer", "ops@test.invalid", ops.Id), default);
            var addAgain = await invitations.AddEvaluatorAsync(supervisor, id, new EvaluatorInput("Someone else", "ops@test.invalid", cs.Id), default);
            var addNowhere = await invitations.AddEvaluatorAsync(supervisor, id, new EvaluatorInput("Nobody", "", 99999), default);
            var addCs = await invitations.AddEvaluatorAsync(supervisor, id, new EvaluatorInput("CS reviewer", "", cs.Id), default);
            var evaluators = (await invitations.EvaluatorsAsync(operation, id, default))!;
            var opsId = evaluators.Single(row => row.DepartmentId == ops.Id).Id;
            var csId = evaluators.Single(row => row.DepartmentId == cs.Id).Id;
            var inDraft = await invitations.GenerateAsync(supervisor, id, new InvitationRequest([opsId], null, null), default);
            check(!addByOperation.Ok && addByOperation.Status == StatusCodes.Status403Forbidden && addOps.Ok && !addAgain.Ok
                && addAgain.Status == StatusCodes.Status409Conflict && !addNowhere.Ok && addCs.Ok && evaluators.Count == 2 && !inDraft.Ok,
                "evaluation links: a supervisor names evaluators by department, once per email; links wait until data preparation");

            await campaigns.MoveAsync(supervisor, id, AnnualEvaluationRules.DataPreparation, null, default);
            await snapshots.GenerateAsync(supervisor, id, null, null, default);
            var byOperation = await invitations.GenerateAsync(operation, id, new InvitationRequest([opsId, csId], null, null), default);
            var issued = await invitations.GenerateAsync(supervisor, id, new InvitationRequest([opsId, csId], null, null), default);
            var twice = await invitations.GenerateAsync(supervisor, id, new InvitationRequest([opsId, csId], null, null), default);
            var opsLink = issued.Links!.Single(link => link.Evaluator == "Ops reviewer");
            var csLink = issued.Links!.Single(link => link.Evaluator == "CS reviewer");
            var stored = await db.EvaluationInvitations.AsNoTracking().Where(row => row.CampaignId == id).ToListAsync();
            check(!byOperation.Ok && issued.Ok && issued.Links!.Count == 2 && twice.Ok && twice.Links!.Count == 0
                && issued.Links.All(link => EvaluationInvitations.LooksLikeToken(link.Token) && link.InvitationId > 0 && link.Carrier == "ALPHA TRANSPORT")
                && stored.Count == 2 && stored.All(row => row.Status == AnnualEvaluationRules.InvitationPending)
                && stored.Single(row => row.Id == opsLink.InvitationId).TokenHash == EvaluationInvitations.HashOf(opsLink.Token)
                && stored.All(row => issued.Links.All(link => row.TokenHash != link.Token))
                && JsonSerializer.Serialize(await invitations.EvaluatorsAsync(supervisor, id, default)).Contains(opsLink.Token) == false,
                "evaluation links: one link per evaluator and carrier, its token in the answer once and never stored or listed; a live pair is not issued twice");

            var notOpen = await external.ReadAsync(opsLink.Token, default);
            var campaign = await db.EvaluationCampaigns.SingleAsync(row => row.Id == id);
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
            campaign.Status = AnnualEvaluationRules.Open;
            campaign.OpenOn = today.AddDays(-1);
            campaign.DueOn = today.AddDays(5);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var unknown = await external.ReadAsync(EvaluationInvitations.NewToken(), default);
            var garbage = await external.ReadAsync("not-a-token", default);
            var none = await external.ReadAsync(null, default);
            var read = await external.ReadAsync(opsLink.Token, default);
            var opened = await db.EvaluationInvitations.AsNoTracking().SingleAsync(row => row.Id == opsLink.InvitationId);
            var page = JsonSerializer.Serialize(read.View);
            check(notOpen.Status == StatusCodes.Status410Gone && unknown.Status == StatusCodes.Status404NotFound
                && garbage.Status == StatusCodes.Status404NotFound && none.Status == StatusCodes.Status404NotFound
                && read.Status == StatusCodes.Status200OK && read.View!.Carrier == "ALPHA TRANSPORT" && read.View.Department == ops.Name
                && !read.View.Submitted && read.View.Questions.Count > 0
                && read.View.Performance.All(metric => ExternalEvaluationService.Shown.Contains(metric.Code))
                && read.View.Performance.Any(metric => metric.Code == "total-jobs" && metric.Value == 2)
                && opened.Status == AnnualEvaluationRules.InvitationOpened && opened.OpenedAt is not null
                && !page.Contains("SECRET-JOB") && !page.Contains("ources") && !page.Contains("ormula") && !page.Contains("\"Id\"")
                && !page.Contains(opsLink.Token),
                "evaluation page: a link opens its own carrier's sheet once the campaign is open, with no records, formulas or ids; anything else is not found");

            var questions = read.View.Questions;
            var missing = await external.SubmitAsync(opsLink.Token, new ExternalSubmission([], ""), default);
            var lowNoReason = await external.SubmitAsync(opsLink.Token, new ExternalSubmission(
                questions.Select(question => new EvaluationInvitations.Answer(question.Code, 1, false, "")).ToList(), ""), default);
            var answers = questions.Select((question, index) => index == 0
                ? new EvaluationInvitations.Answer(question.Code, null, true, "")
                : new EvaluationInvitations.Answer(question.Code, 4, false, "steady")).ToList();
            var submitted = await external.SubmitAsync(opsLink.Token, new ExternalSubmission(answers, "Good year overall"), default);
            var again = await external.SubmitAsync(opsLink.Token, new ExternalSubmission(answers, "second go"), default);
            var reread = await external.ReadAsync(opsLink.Token, default);
            var response = await db.EvaluationResponses.AsNoTracking().SingleAsync(row => row.InvitationId == opsLink.InvitationId);
            var savedAnswers = await db.EvaluationAnswers.AsNoTracking().Where(row => row.ResponseId == response.Id).ToListAsync();
            var revokeAnswered = await invitations.RevokeAsync(supervisor, id, opsLink.InvitationId, "changed my mind", default);
            check(missing.Status == StatusCodes.Status400BadRequest && lowNoReason.Status == StatusCodes.Status400BadRequest
                && submitted.Status == StatusCodes.Status200OK && submitted.View!.Submitted && again.Status == StatusCodes.Status409Conflict
                && reread.Status == StatusCodes.Status200OK && reread.View!.Submitted && reread.View.Comment == "Good year overall"
                && reread.View.Answers.Count == questions.Count && reread.View.Answers.Count(answer => answer.NotApplicable) == 1
                && response.Comment == "Good year overall" && response.DepartmentId == ops.Id && response.EvaluationCarrierId == carrierId
                && savedAnswers.Count == questions.Count && savedAnswers.Count(row => row.Rating is null) == 1
                && !revokeAnswered.Ok && revokeAnswered.Status == StatusCodes.Status409Conflict,
                "evaluation sheet: refused until complete, saved once — N/A kept as no rating — then read back, never replaced; an answered link cannot be revoked");

            var revokeShort = await invitations.RevokeAsync(supervisor, id, csLink.InvitationId, "no", default);
            var revoked = await invitations.RevokeAsync(supervisor, id, csLink.InvitationId, "sent to the wrong person", default);
            var afterRevoke = await external.ReadAsync(csLink.Token, default);
            var renewed = await invitations.RenewAsync(supervisor, id, csLink.InvitationId, default);
            var fresh = renewed.Links!.Single();
            var oldStill = await external.ReadAsync(csLink.Token, default);
            var freshRead = await external.ReadAsync(fresh.Token, default);
            check(!revokeShort.Ok && revoked.Ok && afterRevoke.Status == StatusCodes.Status410Gone && renewed.Ok && fresh.Token != csLink.Token
                && oldStill.Status == StatusCodes.Status410Gone && freshRead.Status == StatusCodes.Status200OK && freshRead.View!.Department == cs.Name,
                "evaluation links: a revoked link stops at once; a new one for the same evaluator works and the old stays dead");

            var row = await db.EvaluationInvitations.SingleAsync(one => one.Id == fresh.InvitationId);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var expired = await external.ReadAsync(fresh.Token, default);
            var badDay = await invitations.ExtendAsync(supervisor, id, fresh.InvitationId, "2020-01-01", default);
            var extended = await invitations.ExtendAsync(supervisor, id, fresh.InvitationId, today.AddDays(3).ToString("yyyy-MM-dd"), default);
            var afterExtend = await external.ReadAsync(fresh.Token, default);
            var marked = await invitations.MarkSentAsync(supervisor, id, fresh.InvitationId, default);
            var list = (await invitations.EvaluatorsAsync(operation, id, default))!;
            var csRows = list.Single(one => one.Id == csId).Invitations;
            check(expired.Status == StatusCodes.Status410Gone && !badDay.Ok && extended.Ok && afterExtend.Status == StatusCodes.Status200OK
                && marked.Ok && csRows.Count == 2 && csRows.Count(one => one.State == AnnualEvaluationRules.InvitationRevoked) == 1
                && csRows.Single(one => one.Id == fresh.InvitationId).SentAt is not null
                && list.Single(one => one.Id == opsId).Invitations.Single().State == AnnualEvaluationRules.InvitationSubmitted,
                "evaluation links: an expired link is refused until extended; the list shows each link's state, the revoked one kept");

            campaign = await db.EvaluationCampaigns.SingleAsync(one => one.Id == id);
            campaign.DueOn = today.AddDays(-1);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var pastDue = await external.SubmitAsync(fresh.Token, new ExternalSubmission(answers, ""), default);
            var answeredPastDue = await external.ReadAsync(opsLink.Token, default);
            check(pastDue.Status == StatusCodes.Status410Gone && answeredPastDue.Status == StatusCodes.Status200OK && answeredPastDue.View!.Submitted,
                "evaluation page: nothing is taken after the closing day, but an evaluator can still read what they sent");

            await scoring.CalculateAsync(supervisor, id, null, null, default);
            var result = (await scoring.ResultAsync(operation, id, carrierId, null, default))!;
            check(result.Row.HumanScore == 80m,
                "evaluation sheet: a submitted sheet is what the department score is worked from — rating 4 is 80, N/A left out");
            check(await db.AuditEvents.CountAsync(one => one.Role == "External Evaluator" && one.EntityId == id.ToString()) >= 2
                && !await db.AuditEvents.AnyAsync(one => one.NewValue.Contains(opsLink.Token) || one.OldValue.Contains(opsLink.Token) || one.Reason.Contains(opsLink.Token)),
                "evaluation page: opening and answering are in the audit trail under the evaluator's name, and no token is");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
