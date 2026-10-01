using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record AnnualEvaluationResult(bool Ok, string Message, int Status = StatusCodes.Status200OK, long? Id = null);

public record CreateCampaignInput(int Year, string? Name, int? CopyFrom);
public record CampaignInput(string? Name, string? PeriodStart, string? PeriodEnd, string? OpenOn, string? DueOn, decimal? SystemWeight,
    decimal? HumanWeight, int? MinimumJobs, decimal? MinimumSystemCoverage, int? CommentRequiredAtOrBelow);
public record KpiBandInput(decimal Threshold, decimal Score);
public record KpiInput(string? Code, decimal Weight, bool Enabled, string? Method, string? Direction, string? Measure, decimal FallbackScore,
    IReadOnlyList<KpiBandInput>? Bands);
public record QuestionDepartmentInput(int DepartmentId, bool Enabled, bool Required, int? CommentRequiredAtOrBelow);
public record QuestionInput(string? Code, string? Text, string? TextTh, decimal Weight, bool Enabled, IReadOnlyList<QuestionDepartmentInput>? Departments);
public record CampaignDepartmentInput(int DepartmentId, decimal Weight, bool Enabled);
public record ScoreBandInput(string? Code, string? Label, decimal MinScore);
public record CarrierChangeInput(string? Action, IReadOnlyList<int>? SupplierIds, string? Reason);
public record DepartmentInput(string? Code, string? Name, bool Active = true);

public record CampaignRow(int Id, string Code, string Name, int Year, string Status, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly? OpenOn,
    DateOnly? DueOn, int Carriers, int Included, bool Locked);
public record KpiView(int Id, string Code, string Name, string NameTh, decimal Weight, bool Enabled, string Method, string Direction, string Measure,
    decimal FallbackScore, IReadOnlyList<KpiBandInput> Bands);
public record QuestionView(int Id, string Code, string Text, string TextTh, decimal Weight, bool Enabled, int Position,
    IReadOnlyList<QuestionDepartmentInput> Departments);
public record CampaignDepartmentView(int DepartmentId, string Code, string Name, decimal Weight, bool Enabled);
public record CampaignView(EvaluationCampaign Campaign, bool Locked, IReadOnlyList<KpiView> Kpis, IReadOnlyList<QuestionView> Questions,
    IReadOnlyList<CampaignDepartmentView> Departments, IReadOnlyList<ScoreBandInput> ScoreBands, IReadOnlyList<string> Problems,
    IReadOnlyList<string> Moves, int Carriers, int Included, bool CanManage, bool CanDecide);
public record EvaluationCarrierRow(int Id, int SupplierId, string Code, string Name, string SupplierStatus, bool IsCarrier, bool Included,
    string ExcludedReason, int? TotalJobs, int? CompletedJobs, string Eligibility, DateTimeOffset? CountedAt, string Decision);

/// <summary>
/// Annual Carrier Evaluation, campaign set-up (1 Oct 2026, Phases 1–2): campaigns, their rules, the carriers in them
/// and the moves between states. Everything a campaign is scored under is a row of the campaign and is locked once it
/// opens (<see cref="AnnualEvaluationRules.Locked"/>); every change is in the audit trail as entity
/// <c>annual-evaluation</c>.
/// </summary>
public class AnnualEvaluationService(ScmosDbContext db, AuditService audit, CarrierDirectory carriers)
{
    private const string Entity = "annual-evaluation";

    /* ------------------------------------------------------------------ reading */

    public async Task<IReadOnlyList<CampaignRow>?> ListAsync(AppUser user, CancellationToken token)
    {
        if (!user.Can(Capability.ViewAnnualEvaluation)) return null;
        var counts = await db.EvaluationCarriers.AsNoTracking().GroupBy(row => row.CampaignId)
            .Select(group => new { group.Key, All = group.Count(), Included = group.Count(row => row.Included) }).ToListAsync(token);
        return (await db.EvaluationCampaigns.AsNoTracking().OrderByDescending(row => row.Year).ThenByDescending(row => row.Id).ToListAsync(token))
            .Select(row =>
            {
                var count = counts.FirstOrDefault(one => one.Key == row.Id);
                return new CampaignRow(row.Id, row.Code, row.Name, row.Year, row.Status, row.PeriodStart, row.PeriodEnd, row.OpenOn, row.DueOn,
                    count?.All ?? 0, count?.Included ?? 0, AnnualEvaluationRules.Locked(row.Status));
            }).ToList();
    }

    public async Task<CampaignView?> ReadAsync(AppUser user, int id, CancellationToken token)
    {
        if (!user.Can(Capability.ViewAnnualEvaluation)) return null;
        var campaign = await db.EvaluationCampaigns.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, token);
        if (campaign is null) return null;
        var config = await ConfigurationAsync(campaign, token);
        var bandsByKpi = config.Bands.ToLookup(band => band.KpiId);
        var kpis = config.Kpis.OrderBy(kpi => kpi.Position).Select(kpi => new KpiView(kpi.Id, kpi.Code, kpi.Name, kpi.NameTh, kpi.Weight, kpi.Enabled,
            kpi.Method, kpi.Direction, kpi.Measure, kpi.FallbackScore,
            bandsByKpi[kpi.Id].OrderByDescending(band => band.Threshold).Select(band => new KpiBandInput(band.Threshold, band.Score)).ToList())).ToList();
        var asked = config.QuestionDepartments.ToLookup(row => row.QuestionId);
        var questions = config.Questions.OrderBy(question => question.Position).Select(question => new QuestionView(question.Id, question.Code,
            question.Text, question.TextTh, question.Weight, question.Enabled, question.Position,
            asked[question.Id].Select(row => new QuestionDepartmentInput(row.DepartmentId, row.Enabled, row.Required, row.CommentRequiredAtOrBelow)).ToList())).ToList();
        var master = await db.EvaluationDepartments.AsNoTracking().OrderBy(row => row.Position).ThenBy(row => row.Id).ToListAsync(token);
        var departments = master.Where(row => row.Active || config.Departments.Any(one => one.DepartmentId == row.Id)).Select(row =>
        {
            var chosen = config.Departments.FirstOrDefault(one => one.DepartmentId == row.Id);
            return new CampaignDepartmentView(row.Id, row.Code, row.Name, chosen?.Weight ?? 1m, chosen?.Enabled ?? false);
        }).ToList();
        var all = await db.EvaluationCarriers.CountAsync(row => row.CampaignId == id, token);
        return new CampaignView(campaign, AnnualEvaluationRules.Locked(campaign.Status), kpis, questions, departments,
            config.ScoreBands.OrderByDescending(band => band.MinScore).Select(band => new ScoreBandInput(band.Code, band.Label, band.MinScore)).ToList(),
            AnnualEvaluationRules.Problems(config), AnnualEvaluationRules.Moves[campaign.Status], all, config.IncludedCarriers,
            user.Can(Capability.ManageAnnualEvaluation), user.Can(Capability.DecideAnnualEvaluation));
    }

    public async Task<IReadOnlyList<EvaluationCarrierRow>?> CarriersAsync(AppUser user, int id, CancellationToken token)
    {
        if (!user.Can(Capability.ViewAnnualEvaluation)) return null;
        return await (from row in db.EvaluationCarriers.AsNoTracking()
                      join supplier in db.Suppliers.AsNoTracking() on row.SupplierId equals supplier.Id
                      where row.CampaignId == id
                      orderby row.Included descending, row.CompletedJobs descending, supplier.Name
                      select new EvaluationCarrierRow(row.Id, row.SupplierId, supplier.Code, supplier.LegalName != "" ? supplier.LegalName : supplier.Name,
                          supplier.Status, supplier.IsCarrier, row.Included, row.ExcludedReason, row.TotalJobs, row.CompletedJobs, row.Eligibility,
                          row.CountedAt, row.Decision)).ToListAsync(token);
    }

    public async Task<IReadOnlyList<EvaluationDepartment>?> DepartmentsAsync(AppUser user, CancellationToken token)
    {
        if (!user.Can(Capability.ViewAnnualEvaluation)) return null;
        await EnsureDepartmentsAsync(token);
        return await db.EvaluationDepartments.AsNoTracking().OrderBy(row => row.Position).ThenBy(row => row.Id).ToListAsync(token);
    }

    /* ------------------------------------------------------------------ campaigns */

    public async Task<AnnualEvaluationResult> CreateAsync(AppUser user, CreateCampaignInput input, CancellationToken token)
    {
        if (!user.Can(Capability.ManageAnnualEvaluation)) return Refused("บัญชีนี้ไม่มีสิทธิ์สร้างแคมเปญประเมิน", StatusCodes.Status403Forbidden);
        if (input.Year is < 2020 or > 2100) return Refused("ปีไม่ถูกต้อง");
        EvaluationCampaign? source = null;
        if (input.CopyFrom is { } from)
        {
            source = await db.EvaluationCampaigns.AsNoTracking().FirstOrDefaultAsync(row => row.Id == from, token);
            if (source is null) return Refused("ไม่พบแคมเปญที่จะคัดลอก", StatusCodes.Status404NotFound);
        }
        await EnsureDepartmentsAsync(token);

        var codes = await db.EvaluationCampaigns.Where(row => row.Year == input.Year).Select(row => row.Code).ToListAsync(token);
        var code = $"AE-{input.Year}";
        for (var n = 2; codes.Contains(code); n++) code = $"AE-{input.Year}-{n}";
        var now = DateTimeOffset.UtcNow;
        var name = Text(input.Name, 200);
        var campaign = new EvaluationCampaign
        {
            Code = code, Name = name.Length > 0 ? name : $"Carrier Annual Evaluation {input.Year}", Year = input.Year,
            PeriodStart = new DateOnly(input.Year, 1, 1), PeriodEnd = new DateOnly(input.Year, 12, 31),
            CreatedBy = user.Signature, CreatedAt = now, UpdatedBy = user.Signature, UpdatedAt = now,
        };
        if (source is not null)
        {
            campaign.SystemWeight = source.SystemWeight;
            campaign.HumanWeight = source.HumanWeight;
            campaign.MinimumJobs = source.MinimumJobs;
            campaign.MinimumSystemCoverage = source.MinimumSystemCoverage;
            campaign.CommentRequiredAtOrBelow = source.CommentRequiredAtOrBelow;
        }
        db.EvaluationCampaigns.Add(campaign);
        await db.SaveChangesAsync(token);

        if (source is null) await SeedDefaultsAsync(campaign.Id, token);
        else await CopyRulesAsync(source.Id, campaign.Id, token);
        await Record(user, campaign.Id, AuditActions.Register, "campaign", "", $"{campaign.Code} · {campaign.Name}"
            + (source is null ? "" : $" · คัดลอกเกณฑ์จาก {source.Code}"), "", token);
        return new AnnualEvaluationResult(true, $"สร้าง {campaign.Code} แล้ว", Id: campaign.Id);
    }

    public async Task<AnnualEvaluationResult> UpdateAsync(AppUser user, int id, CampaignInput input, CancellationToken token)
    {
        var (campaign, refusal) = await EditableAsync(user, id, token);
        if (campaign is null) return refusal!;
        var changes = new List<string>();
        void Set<T>(string label, T before, T after, Action<T> apply)
        {
            if (EqualityComparer<T>.Default.Equals(before, after)) return;
            changes.Add($"{label}: {before} → {after}");
            apply(after);
        }

        if (input.Name is not null)
        {
            var name = Text(input.Name, 200);
            if (name.Length == 0) return Refused("ต้องมีชื่อแคมเปญ");
            Set("ชื่อ", campaign.Name, name, value => campaign.Name = value);
        }
        foreach (var (label, text, read, write) in new (string, string?, Func<DateOnly?>, Action<DateOnly?>)[]
        {
            ("เริ่มช่วงประเมิน", input.PeriodStart, () => campaign.PeriodStart, value => campaign.PeriodStart = value!.Value),
            ("สิ้นสุดช่วงประเมิน", input.PeriodEnd, () => campaign.PeriodEnd, value => campaign.PeriodEnd = value!.Value),
            ("วันเปิด", input.OpenOn, () => campaign.OpenOn, value => campaign.OpenOn = value),
            ("วันปิด", input.DueOn, () => campaign.DueOn, value => campaign.DueOn = value),
        })
        {
            if (text is null) continue;
            var optional = label is "วันเปิด" or "วันปิด";
            DateOnly? day = text.Trim().Length == 0 && optional ? null : Day(text);
            if (day is null && !(optional && text.Trim().Length == 0)) return Refused($"{label}: วันที่ไม่ถูกต้อง (ใช้ YYYY-MM-DD)");
            Set(label, read(), day, write);
        }
        if (input.SystemWeight is { } system) Set("น้ำหนัก System", campaign.SystemWeight, system, value => campaign.SystemWeight = value);
        if (input.HumanWeight is { } human) Set("น้ำหนัก Department", campaign.HumanWeight, human, value => campaign.HumanWeight = value);
        if (input.MinimumJobs is { } minimum) Set("งานขั้นต่ำ", campaign.MinimumJobs, minimum, value => campaign.MinimumJobs = value);
        if (input.MinimumSystemCoverage is { } coverage)
            Set("KPI ที่วัดได้ขั้นต่ำ (%)", campaign.MinimumSystemCoverage, coverage, value => campaign.MinimumSystemCoverage = value);
        if (input.CommentRequiredAtOrBelow is { } comment)
            Set("บังคับความเห็นเมื่อคะแนน ≤", campaign.CommentRequiredAtOrBelow, comment, value => campaign.CommentRequiredAtOrBelow = value);
        if (changes.Count == 0) return new AnnualEvaluationResult(true, "ไม่มีอะไรเปลี่ยน", Id: id);

        Touch(campaign, user);
        await db.SaveChangesAsync(token);
        // A changed period changes every carrier's count: they are counted again rather than left saying the old one.
        if (changes.Any(change => change.StartsWith("เริ่มช่วง") || change.StartsWith("สิ้นสุดช่วง") || change.StartsWith("งานขั้นต่ำ")))
            await CountAsync(campaign, token);
        await Record(user, id, AuditActions.Update, "campaign", "", string.Join(" · ", changes), "", token);
        return new AnnualEvaluationResult(true, "บันทึกแคมเปญแล้ว", Id: id);
    }

    /// <summary>The seven KPIs, all of them every time: one may be switched off, none may be added that the snapshot cannot read.</summary>
    public async Task<AnnualEvaluationResult> SaveKpisAsync(AppUser user, int id, IReadOnlyList<KpiInput>? input, CancellationToken token)
    {
        var (campaign, refusal) = await EditableAsync(user, id, token);
        if (campaign is null) return refusal!;
        var given = input ?? [];
        var codes = given.Select(kpi => (kpi.Code ?? "").Trim()).ToList();
        if (codes.Distinct().Count() != codes.Count || codes.Any(code => !AnnualEvaluationRules.IsKpi(code)))
            return Refused("รายการ KPI ไม่ถูกต้อง");
        foreach (var kpi in given)
        {
            if (kpi.Weight is < 0 or > 100) return Refused($"KPI {kpi.Code}: น้ำหนักต้องอยู่ระหว่าง 0–100");
            if (kpi.Method is not (AnnualEvaluationRules.Band or AnnualEvaluationRules.Manual)) return Refused($"KPI {kpi.Code}: วิธีให้คะแนนไม่ถูกต้อง");
            if (kpi.Direction is not (AnnualEvaluationRules.Higher or AnnualEvaluationRules.Lower)) return Refused($"KPI {kpi.Code}: ทิศทางไม่ถูกต้อง");
            if (kpi.FallbackScore is < 0 or > 100 || (kpi.Bands ?? []).Any(band => band.Score is < 0 or > 100))
                return Refused($"KPI {kpi.Code}: คะแนนต้องอยู่ระหว่าง 0–100");
            if ((kpi.Bands ?? []).Count > 20) return Refused($"KPI {kpi.Code}: ช่วงคะแนนมากเกินไป");
        }

        var rows = await db.EvaluationKpis.Where(row => row.CampaignId == id).ToListAsync(token);
        var ids = rows.Select(row => row.Id).ToList();
        db.EvaluationKpiBands.RemoveRange(await db.EvaluationKpiBands.Where(band => ids.Contains(band.KpiId)).ToListAsync(token));
        var summary = new List<string>();
        foreach (var kpi in given)
        {
            var row = rows.FirstOrDefault(one => one.Code == kpi.Code);
            if (row is null) continue;
            if (row.Weight != kpi.Weight || row.Enabled != kpi.Enabled) summary.Add($"{row.Code} {row.Weight:0.##}{(row.Enabled ? "" : " ปิด")} → {kpi.Weight:0.##}{(kpi.Enabled ? "" : " ปิด")}");
            row.Weight = kpi.Weight;
            row.Enabled = kpi.Enabled;
            row.Method = kpi.Method!;
            row.Direction = kpi.Direction!;
            row.FallbackScore = kpi.FallbackScore;
            if (kpi.Measure is not null) row.Measure = Text(kpi.Measure, 500);
            db.EvaluationKpiBands.AddRange((kpi.Bands ?? []).Select(band => new EvaluationKpiBand { KpiId = row.Id, Threshold = band.Threshold, Score = band.Score }));
        }
        Touch(campaign, user);
        await db.SaveChangesAsync(token);
        await Record(user, id, AuditActions.Configure, "kpis", "", summary.Count > 0 ? string.Join(" · ", summary) : "ช่วงคะแนน / วิธีให้คะแนน", "", token);
        return new AnnualEvaluationResult(true, "บันทึก KPI แล้ว", Id: id);
    }

    public async Task<AnnualEvaluationResult> SaveQuestionsAsync(AppUser user, int id, IReadOnlyList<QuestionInput>? input, CancellationToken token)
    {
        var (campaign, refusal) = await EditableAsync(user, id, token);
        if (campaign is null) return refusal!;
        var given = (input ?? []).ToList();
        if (given.Count is 0 or > 30) return Refused("ต้องมีคำถาม 1–30 ข้อ");
        var departments = await db.EvaluationDepartments.AsNoTracking().Select(row => row.Id).ToListAsync(token);
        var codes = new List<string>();
        foreach (var question in given)
        {
            var code = AnnualEvaluationRules.Slug(question.Code is { Length: > 0 } ? question.Code : question.Text);
            if (code.Length == 0 || Text(question.Text, 300).Length == 0) return Refused("ทุกคำถามต้องมีข้อความ");
            if (codes.Contains(code)) return Refused($"รหัสคำถาม {code} ซ้ำกัน");
            if (question.Weight is < 0 or > 100) return Refused($"คำถาม {question.Text}: น้ำหนักต้องอยู่ระหว่าง 0–100");
            if ((question.Departments ?? []).Any(row => !departments.Contains(row.DepartmentId) || row.CommentRequiredAtOrBelow is < 0 or > 5))
                return Refused($"คำถาม {question.Text}: แผนกไม่ถูกต้อง");
            codes.Add(code);
        }

        var rows = await db.EvaluationQuestions.Where(row => row.CampaignId == id).ToListAsync(token);
        var rowIds = rows.Select(row => row.Id).ToList();
        if (await db.EvaluationAnswers.AnyAsync(answer => rowIds.Contains(answer.QuestionId), token))
            return Refused("มีคำตอบแล้ว — แก้คำถามไม่ได้", StatusCodes.Status409Conflict);
        db.EvaluationQuestionDepartments.RemoveRange(await db.EvaluationQuestionDepartments.Where(row => rowIds.Contains(row.QuestionId)).ToListAsync(token));
        db.EvaluationQuestions.RemoveRange(rows.Where(row => !codes.Contains(row.Code)));
        await db.SaveChangesAsync(token);

        var saved = new List<(EvaluationQuestion Row, QuestionInput Input)>();
        foreach (var (question, position) in given.Select((question, position) => (question, position)))
        {
            var code = codes[position];
            var row = rows.FirstOrDefault(one => one.Code == code);
            if (row is null)
            {
                row = new EvaluationQuestion { CampaignId = id, Code = code };
                db.EvaluationQuestions.Add(row);
            }
            row.Text = Text(question.Text, 300);
            row.TextTh = Text(question.TextTh, 300);
            row.Weight = question.Weight;
            row.Enabled = question.Enabled;
            row.Position = position;
            saved.Add((row, question));
        }
        await db.SaveChangesAsync(token);
        foreach (var (row, question) in saved)
            db.EvaluationQuestionDepartments.AddRange((question.Departments ?? []).DistinctBy(one => one.DepartmentId).Select(one => new EvaluationQuestionDepartment
            {
                QuestionId = row.Id, DepartmentId = one.DepartmentId, Enabled = one.Enabled, Required = one.Required,
                CommentRequiredAtOrBelow = one.CommentRequiredAtOrBelow,
            }));
        Touch(campaign, user);
        await db.SaveChangesAsync(token);
        await Record(user, id, AuditActions.Configure, "questions", "",
            string.Join(" · ", saved.Select(one => $"{one.Row.Code} {one.Row.Weight:0.##}{(one.Row.Enabled ? "" : " ปิด")}")), "", token);
        return new AnnualEvaluationResult(true, "บันทึกคำถามแล้ว", Id: id);
    }

    public async Task<AnnualEvaluationResult> SaveDepartmentsAsync(AppUser user, int id, IReadOnlyList<CampaignDepartmentInput>? input,
        CancellationToken token)
    {
        var (campaign, refusal) = await EditableAsync(user, id, token);
        if (campaign is null) return refusal!;
        var given = (input ?? []).DistinctBy(row => row.DepartmentId).ToList();
        var known = await db.EvaluationDepartments.AsNoTracking().Select(row => row.Id).ToListAsync(token);
        if (given.Any(row => !known.Contains(row.DepartmentId) || row.Weight is < 0 or > 1000)) return Refused("แผนกหรือน้ำหนักไม่ถูกต้อง");
        var rows = await db.EvaluationCampaignDepartments.Where(row => row.CampaignId == id).ToListAsync(token);
        foreach (var department in given)
        {
            var row = rows.FirstOrDefault(one => one.DepartmentId == department.DepartmentId);
            if (row is null) db.EvaluationCampaignDepartments.Add(row = new EvaluationCampaignDepartment { CampaignId = id, DepartmentId = department.DepartmentId });
            row.Weight = department.Weight;
            row.Enabled = department.Enabled;
        }
        Touch(campaign, user);
        await db.SaveChangesAsync(token);
        await Record(user, id, AuditActions.Configure, "departments", "",
            string.Join(" · ", given.Select(row => $"{row.DepartmentId}={row.Weight:0.##}{(row.Enabled ? "" : " ปิด")}")), "", token);
        return new AnnualEvaluationResult(true, "บันทึกน้ำหนักแผนกแล้ว", Id: id);
    }

    public async Task<AnnualEvaluationResult> SaveScoreBandsAsync(AppUser user, int id, IReadOnlyList<ScoreBandInput>? input, CancellationToken token)
    {
        var (campaign, refusal) = await EditableAsync(user, id, token);
        if (campaign is null) return refusal!;
        var given = (input ?? []).ToList();
        if (given.Count > 10) return Refused("ช่วงคะแนนมากเกินไป");
        var bands = new List<EvaluationScoreBand>();
        foreach (var band in given)
        {
            var label = Text(band.Label, 120);
            var code = AnnualEvaluationRules.Slug(band.Code is { Length: > 0 } ? band.Code : label);
            if (code.Length == 0 || label.Length == 0) return Refused("ทุกช่วงต้องมีชื่อ");
            if (band.MinScore is < 0 or > 100) return Refused($"{label}: คะแนนเริ่มต้องอยู่ระหว่าง 0–100");
            if (bands.Any(one => one.Code == code || one.MinScore == band.MinScore)) return Refused($"{label}: ซ้ำกับช่วงอื่น");
            bands.Add(new EvaluationScoreBand { CampaignId = id, Code = code, Label = label, MinScore = band.MinScore });
        }
        db.EvaluationScoreBands.RemoveRange(await db.EvaluationScoreBands.Where(row => row.CampaignId == id).ToListAsync(token));
        await db.SaveChangesAsync(token);
        db.EvaluationScoreBands.AddRange(bands);
        Touch(campaign, user);
        await db.SaveChangesAsync(token);
        await Record(user, id, AuditActions.Configure, "score-bands", "",
            string.Join(" · ", bands.OrderByDescending(band => band.MinScore).Select(band => $"{band.Label} ≥ {band.MinScore:0.##}")), "", token);
        return new AnnualEvaluationResult(true, "บันทึกช่วงคะแนนรวมแล้ว", Id: id);
    }

    /// <summary>A department added to the list everybody's campaigns choose from. Its weight is each campaign's own.</summary>
    public async Task<AnnualEvaluationResult> SaveDepartmentAsync(AppUser user, DepartmentInput input, CancellationToken token)
    {
        if (!user.Can(Capability.ManageAnnualEvaluation)) return Refused("บัญชีนี้ไม่มีสิทธิ์แก้รายการแผนก", StatusCodes.Status403Forbidden);
        var name = Text(input.Name, 120);
        var code = AnnualEvaluationRules.Slug(input.Code is { Length: > 0 } ? input.Code : name);
        if (code.Length == 0 || name.Length == 0) return Refused("ระบุชื่อแผนก");
        await EnsureDepartmentsAsync(token);
        var row = await db.EvaluationDepartments.FirstOrDefaultAsync(one => one.Code == code, token);
        var added = row is null;
        if (row is null)
        {
            row = new EvaluationDepartment { Code = code, Position = (await db.EvaluationDepartments.MaxAsync(one => (int?)one.Position, token) ?? -1) + 1 };
            db.EvaluationDepartments.Add(row);
        }
        row.Name = name;
        row.Active = input.Active;
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.Configure, Entity, "department:" + row.Id, name, "department", "",
            (added ? "เพิ่ม " : "") + (input.Active ? "ใช้งาน" : "ปิด"), "", token);
        return new AnnualEvaluationResult(true, added ? $"เพิ่มแผนก {name} แล้ว" : $"บันทึกแผนก {name} แล้ว", Id: row.Id);
    }

    /* ------------------------------------------------------------------ carriers */

    /// <summary>
    /// add-eligible — every approved carrier in the Supplier Register not yet in the campaign · add · include · exclude
    /// (with a reason) · remove (only while nothing has been recorded against it). Counted again afterwards.
    /// </summary>
    public async Task<AnnualEvaluationResult> ChangeCarriersAsync(AppUser user, int id, CarrierChangeInput input, CancellationToken token)
    {
        var (campaign, refusal) = await EditableAsync(user, id, token);
        if (campaign is null) return refusal!;
        var action = (input.Action ?? "").Trim().ToLowerInvariant();
        var wanted = (input.SupplierIds ?? []).Distinct().Take(1000).ToList();
        var reason = Text(input.Reason, 500);
        var rows = await db.EvaluationCarriers.Where(row => row.CampaignId == id).ToListAsync(token);
        var now = DateTimeOffset.UtcNow;
        string summary;
        switch (action)
        {
            case "add-eligible":
            {
                var present = rows.Select(row => row.SupplierId).ToHashSet();
                var eligible = await db.Suppliers.AsNoTracking().Where(row => row.IsCarrier && row.Status == "approved")
                    .Select(row => row.Id).ToListAsync(token);
                var fresh = eligible.Where(supplier => !present.Contains(supplier)).ToList();
                db.EvaluationCarriers.AddRange(fresh.Select(supplier => new EvaluationCarrier { CampaignId = id, SupplierId = supplier, AddedBy = user.Signature, AddedAt = now }));
                summary = $"เพิ่มผู้ขนส่งที่อนุมัติแล้ว {fresh.Count} ราย";
                break;
            }
            case "add":
            {
                if (wanted.Count == 0) return Refused("เลือกผู้ขนส่งก่อน");
                var known = await db.Suppliers.AsNoTracking().Where(row => wanted.Contains(row.Id) && row.IsCarrier).Select(row => row.Id).ToListAsync(token);
                if (known.Count != wanted.Count) return Refused("เพิ่มได้เฉพาะผู้ขนส่งในทะเบียน Supplier");
                var fresh = known.Where(supplier => rows.All(row => row.SupplierId != supplier)).ToList();
                db.EvaluationCarriers.AddRange(fresh.Select(supplier => new EvaluationCarrier { CampaignId = id, SupplierId = supplier, AddedBy = user.Signature, AddedAt = now }));
                summary = $"เพิ่มผู้ขนส่ง {fresh.Count} ราย";
                break;
            }
            case "exclude" or "include":
            {
                var chosen = rows.Where(row => wanted.Contains(row.SupplierId)).ToList();
                if (chosen.Count == 0) return Refused("เลือกผู้ขนส่งก่อน");
                if (action == "exclude" && reason.Length == 0) return Refused("ระบุเหตุผลที่ไม่ประเมิน");
                foreach (var row in chosen)
                {
                    row.Included = action == "include";
                    row.ExcludedReason = action == "include" ? "" : reason;
                }
                summary = (action == "include" ? "นำกลับมาประเมิน " : "ไม่ประเมิน ") + chosen.Count + " ราย";
                break;
            }
            case "remove":
            {
                var chosen = rows.Where(row => wanted.Contains(row.SupplierId)).ToList();
                if (chosen.Count == 0) return Refused("เลือกผู้ขนส่งก่อน");
                var chosenIds = chosen.Select(row => row.Id).ToList();
                if (await db.EvaluationSnapshots.AnyAsync(row => chosenIds.Contains(row.EvaluationCarrierId), token)
                    || await db.EvaluationInvitations.AnyAsync(row => chosenIds.Contains(row.EvaluationCarrierId), token))
                    return Refused("มีข้อมูลบันทึกไว้แล้ว — ใช้ \"ไม่ประเมิน\" แทนการลบ", StatusCodes.Status409Conflict);
                db.EvaluationCarriers.RemoveRange(chosen);
                summary = $"เอาออก {chosen.Count} ราย";
                break;
            }
            default:
                return Refused("คำสั่งไม่ถูกต้อง");
        }
        Touch(campaign, user);
        await db.SaveChangesAsync(token);
        await CountAsync(campaign, token);
        await Record(user, id, AuditActions.CarrierChange, "carriers", "", summary, reason, token);
        return new AnnualEvaluationResult(true, summary, Id: id);
    }

    public async Task<AnnualEvaluationResult> RecountAsync(AppUser user, int id, CancellationToken token)
    {
        var (campaign, refusal) = await EditableAsync(user, id, token);
        if (campaign is null) return refusal!;
        var counted = await CountAsync(campaign, token);
        return new AnnualEvaluationResult(true, $"นับงานใหม่ {counted} ราย", Id: id);
    }

    /// <summary>
    /// Each carrier's jobs in the period, read off the register through the carrier directory — the one reading of
    /// "which company is this spelling" every other screen uses. Cancelled jobs are not jobs; completed is the
    /// register's own <see cref="JobRules.IsDone"/>.
    /// </summary>
    private async Task<int> CountAsync(EvaluationCampaign campaign, CancellationToken token)
    {
        var rows = await db.EvaluationCarriers.Where(row => row.CampaignId == campaign.Id).ToListAsync(token);
        if (rows.Count == 0) return 0;
        var directory = await carriers.ReadAsync(token);
        var suppliers = await db.Suppliers.AsNoTracking().Select(row => new { row.Id, row.Name }).ToListAsync(token);
        var idOf = suppliers.GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Min(row => row.Id), StringComparer.OrdinalIgnoreCase);
        var from = Number(campaign.PeriodStart);
        var to = Number(campaign.PeriodEnd);
        var total = new Dictionary<int, int>();
        var completed = new Dictionary<int, int>();
        foreach (var job in await db.OperationJobs.AsNoTracking().Where(row => row.Trucker != "")
                     .Select(row => new { row.Trucker, row.WorkDate, row.Status }).ToListAsync(token))
        {
            var day = Formats.DateNumber(job.WorkDate);
            if (day < from || day > to || WorkspaceTabs.IsCancelled(job.Status)) continue;
            if (!idOf.TryGetValue(directory.Company(job.Trucker), out var supplier)) continue;
            total[supplier] = total.GetValueOrDefault(supplier) + 1;
            if (JobRules.IsDone(job.Status)) completed[supplier] = completed.GetValueOrDefault(supplier) + 1;
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            row.TotalJobs = total.GetValueOrDefault(row.SupplierId);
            row.CompletedJobs = completed.GetValueOrDefault(row.SupplierId);
            row.Eligibility = AnnualEvaluationRules.Eligibility(row.CompletedJobs.Value, campaign.MinimumJobs);
            row.CountedAt = now;
        }
        await db.SaveChangesAsync(token);
        return rows.Count;
    }

    /* ------------------------------------------------------------------ states */

    public async Task<AnnualEvaluationResult> MoveAsync(AppUser user, int id, string? to, string? reason, CancellationToken token)
    {
        var wanted = (to ?? "").Trim().ToLowerInvariant();
        var campaign = await db.EvaluationCampaigns.FirstOrDefaultAsync(row => row.Id == id, token);
        if (campaign is null) return Refused("ไม่พบแคมเปญนี้", StatusCodes.Status404NotFound);
        var allowed = AnnualEvaluationRules.IsDecision(wanted)
            ? user.Can(Capability.DecideAnnualEvaluation)
            : user.Can(Capability.ManageAnnualEvaluation);
        if (!allowed) return Refused("บัญชีนี้ไม่มีสิทธิ์เปลี่ยนสถานะนี้", StatusCodes.Status403Forbidden);
        if (!AnnualEvaluationRules.CanMove(campaign.Status, wanted)) return Refused($"เปลี่ยนจาก {campaign.Status} เป็น {wanted} ไม่ได้");
        var text = Text(reason, 500);
        var backward = Array.IndexOf(AnnualEvaluationRules.Statuses, wanted) < Array.IndexOf(AnnualEvaluationRules.Statuses, campaign.Status);
        if (backward && text.Length == 0) return Refused("ย้อนสถานะต้องระบุเหตุผล");
        if (wanted is AnnualEvaluationRules.Ready or AnnualEvaluationRules.Open)
        {
            var problems = AnnualEvaluationRules.Problems(await ConfigurationAsync(campaign, token));
            if (problems.Count > 0) return Refused("ยังเปิดไม่ได้: " + string.Join(" · ", problems.Take(3)) + (problems.Count > 3 ? $" (+{problems.Count - 3})" : ""));
        }

        var before = campaign.Status;
        campaign.Status = wanted;
        if (wanted == AnnualEvaluationRules.Open && campaign.LockedAt is null)
        {
            campaign.LockedAt = DateTimeOffset.UtcNow;
            campaign.LockedBy = user.Signature;
        }
        campaign.UpdatedBy = user.Signature;
        campaign.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        await Record(user, id, AuditActions.StatusChange, "status", before, wanted, text, token);
        return new AnnualEvaluationResult(true, $"{campaign.Code}: {before} → {wanted}", Id: id);
    }

    /* ------------------------------------------------------------------ helpers */

    private async Task<AnnualEvaluationRules.Configuration> ConfigurationAsync(EvaluationCampaign campaign, CancellationToken token)
    {
        var id = campaign.Id;
        var kpis = await db.EvaluationKpis.AsNoTracking().Where(row => row.CampaignId == id).ToListAsync(token);
        var kpiIds = kpis.Select(row => row.Id).ToList();
        var questions = await db.EvaluationQuestions.AsNoTracking().Where(row => row.CampaignId == id).ToListAsync(token);
        var questionIds = questions.Select(row => row.Id).ToList();
        return new AnnualEvaluationRules.Configuration(campaign, kpis,
            await db.EvaluationKpiBands.AsNoTracking().Where(row => kpiIds.Contains(row.KpiId)).ToListAsync(token),
            questions,
            await db.EvaluationQuestionDepartments.AsNoTracking().Where(row => questionIds.Contains(row.QuestionId)).ToListAsync(token),
            await db.EvaluationCampaignDepartments.AsNoTracking().Where(row => row.CampaignId == id).ToListAsync(token),
            await db.EvaluationScoreBands.AsNoTracking().Where(row => row.CampaignId == id).ToListAsync(token),
            await db.EvaluationCarriers.CountAsync(row => row.CampaignId == id && row.Included, token));
    }

    /// <summary>The campaign, when this person may change its rules and they are not yet locked.</summary>
    private async Task<(EvaluationCampaign? Campaign, AnnualEvaluationResult? Refusal)> EditableAsync(AppUser user, int id, CancellationToken token)
    {
        if (!user.Can(Capability.ManageAnnualEvaluation)) return (null, Refused("บัญชีนี้ไม่มีสิทธิ์แก้แคมเปญประเมิน", StatusCodes.Status403Forbidden));
        var campaign = await db.EvaluationCampaigns.FirstOrDefaultAsync(row => row.Id == id, token);
        if (campaign is null) return (null, Refused("ไม่พบแคมเปญนี้", StatusCodes.Status404NotFound));
        if (AnnualEvaluationRules.Locked(campaign.Status))
            return (null, Refused("แคมเปญเปิดแล้ว — เกณฑ์และรายชื่อถูกล็อก แก้ได้ด้วยการคำนวณใหม่พร้อมเหตุผลเท่านั้น", StatusCodes.Status409Conflict));
        return (campaign, null);
    }

    private async Task EnsureDepartmentsAsync(CancellationToken token)
    {
        if (await db.EvaluationDepartments.AnyAsync(token)) return;
        db.EvaluationDepartments.AddRange(AnnualEvaluationRules.Departments.Select((row, position) =>
            new EvaluationDepartment { Code = row.Code, Name = row.Name, Position = position }));
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); }   // another request seeded them first
    }

    private async Task SeedDefaultsAsync(int id, CancellationToken token)
    {
        var kpis = AnnualEvaluationRules.Kpis.Select((kpi, position) => (Default: kpi, Row: new EvaluationKpi
        {
            CampaignId = id, Code = kpi.Code, Name = kpi.Name, NameTh = kpi.NameTh, Weight = kpi.Weight, Method = kpi.Method,
            Direction = kpi.Direction, Measure = kpi.Measure, Position = position,
        })).ToList();
        db.EvaluationKpis.AddRange(kpis.Select(kpi => kpi.Row));
        var departments = await db.EvaluationDepartments.AsNoTracking().Where(row => row.Active).ToListAsync(token);
        db.EvaluationCampaignDepartments.AddRange(departments.Select(row => new EvaluationCampaignDepartment { CampaignId = id, DepartmentId = row.Id }));
        var questions = AnnualEvaluationRules.Questions.Select((question, position) => (Default: question, Row: new EvaluationQuestion
        {
            CampaignId = id, Code = question.Code, Text = question.Text, TextTh = question.TextTh, Weight = question.Weight, Position = position,
        })).ToList();
        db.EvaluationQuestions.AddRange(questions.Select(question => question.Row));
        await db.SaveChangesAsync(token);

        foreach (var (kpi, row) in kpis)
            db.EvaluationKpiBands.AddRange(kpi.Bands.Select(band => new EvaluationKpiBand { KpiId = row.Id, Threshold = band.Threshold, Score = band.Score }));
        foreach (var (question, row) in questions)
            db.EvaluationQuestionDepartments.AddRange(departments.Select(department => new EvaluationQuestionDepartment
            {
                QuestionId = row.Id, DepartmentId = department.Id,
                Enabled = question.AskedOf.Length == 0 || question.AskedOf.Contains(department.Code),
            }));
        await db.SaveChangesAsync(token);
    }

    private async Task CopyRulesAsync(int from, int to, CancellationToken token)
    {
        var kpis = await db.EvaluationKpis.AsNoTracking().Where(row => row.CampaignId == from).ToListAsync(token);
        var kpiIds = kpis.Select(row => row.Id).ToList();
        var bands = await db.EvaluationKpiBands.AsNoTracking().Where(row => kpiIds.Contains(row.KpiId)).ToListAsync(token);
        var questions = await db.EvaluationQuestions.AsNoTracking().Where(row => row.CampaignId == from).ToListAsync(token);
        var questionIds = questions.Select(row => row.Id).ToList();
        var asked = await db.EvaluationQuestionDepartments.AsNoTracking().Where(row => questionIds.Contains(row.QuestionId)).ToListAsync(token);

        var newKpis = kpis.Select(row => (Old: row.Id, Row: new EvaluationKpi
        {
            CampaignId = to, Code = row.Code, Name = row.Name, NameTh = row.NameTh, Weight = row.Weight, Enabled = row.Enabled, Method = row.Method,
            Direction = row.Direction, Measure = row.Measure, FallbackScore = row.FallbackScore, Position = row.Position,
        })).ToList();
        var newQuestions = questions.Select(row => (Old: row.Id, Row: new EvaluationQuestion
        {
            CampaignId = to, Code = row.Code, Text = row.Text, TextTh = row.TextTh, Weight = row.Weight, Enabled = row.Enabled, Position = row.Position,
        })).ToList();
        db.EvaluationKpis.AddRange(newKpis.Select(one => one.Row));
        db.EvaluationQuestions.AddRange(newQuestions.Select(one => one.Row));
        db.EvaluationCampaignDepartments.AddRange((await db.EvaluationCampaignDepartments.AsNoTracking().Where(row => row.CampaignId == from).ToListAsync(token))
            .Select(row => new EvaluationCampaignDepartment { CampaignId = to, DepartmentId = row.DepartmentId, Weight = row.Weight, Enabled = row.Enabled }));
        db.EvaluationScoreBands.AddRange((await db.EvaluationScoreBands.AsNoTracking().Where(row => row.CampaignId == from).ToListAsync(token))
            .Select(row => new EvaluationScoreBand { CampaignId = to, Code = row.Code, Label = row.Label, MinScore = row.MinScore }));
        await db.SaveChangesAsync(token);

        foreach (var (old, row) in newKpis)
            db.EvaluationKpiBands.AddRange(bands.Where(band => band.KpiId == old).Select(band => new EvaluationKpiBand { KpiId = row.Id, Threshold = band.Threshold, Score = band.Score }));
        foreach (var (old, row) in newQuestions)
            db.EvaluationQuestionDepartments.AddRange(asked.Where(one => one.QuestionId == old).Select(one => new EvaluationQuestionDepartment
            {
                QuestionId = row.Id, DepartmentId = one.DepartmentId, Enabled = one.Enabled, Required = one.Required,
                CommentRequiredAtOrBelow = one.CommentRequiredAtOrBelow,
            }));
        await db.SaveChangesAsync(token);
    }

    private static void Touch(EvaluationCampaign campaign, AppUser user)
    {
        campaign.Version++;
        campaign.UpdatedBy = user.Signature;
        campaign.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private Task Record(AppUser user, int id, string action, string field, string before, string after, string reason, CancellationToken token) =>
        audit.RecordAsync(user, action, Entity, id.ToString(CultureInfo.InvariantCulture), "campaign " + id, field, before, after, reason, token);

    private static AnnualEvaluationResult Refused(string message, int status = StatusCodes.Status400BadRequest) => new(false, message, status);

    private static string Text(string? value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length > max ? text[..max] : text;
    }

    private static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact((text ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private static int Number(DateOnly day) => day.Year * 10000 + day.Month * 100 + day.Day;
}
