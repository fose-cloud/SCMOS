using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Booking;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// The Booking Agent (Agent Platform, 28 Sep 2026): what the model proposes is
/// kept only when its own words say it — dates, times, numbers and names each
/// checked their way, a customer only when the register knows it; the reader's
/// answer parsed without trust; the one system identity the audit allows and
/// nothing wider; the pasted-text read under the flag, governance, limiter and
/// audit; and with <c>--write-local-db</c> the pass over unplaced mail on a
/// scratch database of its own.
/// </summary>
static class BookingChecks
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-28T03:00:00Z");
    private static readonly DateOnly Today = new(2026, 9, 28);
    private static readonly AppUser Operator = new("book-op", "op@test.invalid", "Operator", Roles.Operation, "OP-B1", "test", true);
    private static readonly AppUser Carrier = Operator with { UserId = "book-cr", Role = Roles.Subcontractor, OperatorId = "" };

    private const string Sample =
        "เรียน Leschaco\nBASF ขอรถรับตู้ 1x40' HC ที่ท่าเรือแหลมฉบัง ส่งโรงงานระยอง วันที่ 29/09/2026 เวลา 08:30 น.\n" +
        "Booking: BKK123456  ตู้ TEMU5246902 น้ำหนัก 20,000 kg";
    private static readonly string[] Customers = ["BASF", "EVONIK THAILAND"];

    private static Dictionary<string, BookingProposal> Proposals(params (string Field, string Value, string Quote)[] items) =>
        items.ToDictionary(one => one.Field, one => new BookingProposal(one.Value, one.Quote), StringComparer.Ordinal);

    private static readonly Dictionary<string, BookingProposal> Good = Proposals(
        ("customer", "BASF", "BASF ขอรถรับตู้"), ("date", "29/09/2026", "วันที่ 29/09/2026"), ("planTime", "08:30", "เวลา 08:30 น."),
        ("type", "1x40' HC", "ตู้ 1x40' HC"), ("destination", "โรงงานระยอง", "ส่งโรงงานระยอง"), ("container", "TEMU5246902", "ตู้ TEMU5246902"),
        ("weight", "20,000 kg", "น้ำหนัก 20,000 kg"), ("cyYard", "แหลมฉบัง", "ท่าเรือแหลมฉบัง"),
        ("jobCode", "J-999", "Job J-999"), ("product", "Chemical", "BASF ขอรถ"));

    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        /* ---------------- verification ---------------- */
        var draft = BookingVerification.Check("IMPORT", Sample, Good, Customers, Today);
        BookingFieldReading Of(BookingDraft d, string field) => d.Fields.Single(one => one.Field == field);
        check(Of(draft, "customer") is { Verified: true, Value: "BASF" } && Of(draft, "date") is { Verified: true, Value: "29/09/2026" }
            && Of(draft, "planTime") is { Verified: true } && Of(draft, "destination") is { Verified: true, Value: "โรงงานระยอง" }
            && Of(draft, "container") is { Verified: true } && Of(draft, "weight") is { Verified: true } && Of(draft, "cyYard") is { Verified: true },
            "booking: every field its own words state is kept — customer, date, time, destination, container, weight, yard");
        var type = JobVehicleType.Canonical("1x40' HC");
        check(JobVehicleType.IsKnown(type) && Of(draft, "type") is { Verified: true } typed && typed.Value == type,
            $"booking: the type as written lands on the department's list ({type}) and goes in as the list spells it");
        check(Of(draft, "jobCode") is { Verified: false, Value: "", Reason: "ข้อความที่อ้างไม่อยู่ในต้นฉบับ" }
            && Of(draft, "product") is { Verified: false, Value: "", Reason: "ค่าที่อ่านได้ไม่ตรงกับข้อความที่อ้าง" }
            && Of(draft, "emptyReturn") is { Verified: false, Proposed: "", Reason: "ไม่พบในข้อความ" },
            "booking: words the text does not hold, a value its words do not say, and nothing at all are all refused, each with its reason");
        check(draft is { Status: BookingDraft.Complete, Missing.Count: 0 } && !draft.Verified.ContainsKey("jobCode") && draft.Verified.Count == 8,
            "booking: the form takes only the eight accepted fields, and the draft is complete");
        var unknown = BookingVerification.Check("IMPORT", "ACME ขอรถ 29/09/2026", Proposals(("customer", "ACME", "ACME ขอรถ"),
            ("date", "29/09/2026", "29/09/2026")), Customers, Today);
        check(Of(unknown, "customer") is { Verified: false, Reason: "ไม่พบชื่อลูกค้านี้ในทะเบียน" }
            && unknown is { Status: BookingDraft.NeedsInformation } && unknown.Missing.SequenceEqual(["customer", "type", "destination"]),
            "booking: a customer the register does not know is not guessed at — the draft needs information, and says which");
        check(Of(BookingVerification.Check("IMPORT", "basf  ขอรถ", Proposals(("customer", "basf", "basf  ขอรถ")), Customers, Today), "customer").Value == "BASF",
            "booking: a known customer goes in as the register spells it, whatever the case or spacing");
        check(BookingVerification.Check("export", "x", Proposals(), Customers, Today).Missing.SequenceEqual(["customer", "date", "type", "plant"])
            && BookingVerification.CategoryOf("delivery") == "DELIVERY" && BookingVerification.CategoryOf("??") == "IMPORT",
            "booking: each category has its own essentials; an unknown category reads as import, as the document reader does");
        var weird = Of(BookingVerification.Check("IMPORT", "ตู้ 40 ฟุตพิเศษ", Proposals(("type", "40 ฟุตพิเศษ", "ตู้ 40 ฟุตพิเศษ")), Customers, Today), "type");
        check(JobVehicleType.IsKnown(JobVehicleType.Canonical("40 ฟุตพิเศษ")) ? weird.Verified : weird.Reason == "ประเภทรถไม่อยู่ในรายการ",
            "booking: a type that does not land on the list is the person's to choose");

        /* ---------------- dates, times, numbers ---------------- */
        bool Has(string text, int y, int m, int d) => BookingVerification.DatesIn(text, Today).Contains(new DateOnly(y, m, d));
        check(Has("29/9/69", 2026, 9, 29) && Has("29-09-2569", 2026, 9, 29) && Has("2026-09-30", 2026, 9, 30) && Has("29.09.26", 2026, 9, 29),
            "booking: slashes, dashes, dots, ISO; Buddhist years long and short");
        check(Has("1 Oct 2026", 2026, 10, 1) && Has("1 October 2026", 2026, 10, 1) && Has("5 Oct", 2026, 10, 5) && Has("30 Sept", 2026, 9, 30),
            "booking: English months, with the year or taking the received year");
        check(Has("30 ก.ย. 69", 2026, 9, 30) && Has("2 ตุลาคม 2569", 2026, 10, 2) && Has("3 ต.ค.", 2026, 10, 3),
            "booking: Thai months, short and long, Buddhist year or none");
        check(Has("พรุ่งนี้ 8 โมง", 2026, 9, 29) && Has("today", 2026, 9, 28) && Has("มะรืนนี้", 2026, 9, 30) && Has("tomorrow", 2026, 9, 29),
            "booking: today, tomorrow and the day after, against the day the text was received");
        check(BookingVerification.DatesIn("15/03", Today).Count == 0 && BookingVerification.DatesIn("31/02/2026", Today).Count == 0
            && BookingVerification.DatesIn("1x40", Today).Count == 0,
            "booking: a yearless date far from the received day, a date that does not exist, and a box size are no dates");
        check(BookingVerification.Supports("date", "29/09/2026", "ส่ง 29/9/69", Today) && !BookingVerification.Supports("date", "30/09/2026", "ส่ง 29/9/69", Today)
            && !BookingVerification.Supports("date", "29/09/2026", "ส่งด่วน", Today),
            "booking: a date is kept only when its words name that very day");
        HashSet<int> T(string text) => BookingVerification.TimesIn(text);
        check(T("08:30").Contains(510) && T("8.30 น.").SetEquals([510]) && T("2pm").Contains(840) && T("2.30pm").SetEquals([870])
            && T("0800 น.").Contains(480) && T("10 น.").Contains(600),
            "booking: clock times, am/pm (not also the morning), four digits and Thai hours");
        check(T("8 โมงเช้า").Contains(480) && T("บ่าย 2").Contains(840) && T("2 ทุ่ม").Contains(1200) && T("เที่ยง").Contains(720)
            && T("9 โมง").Contains(540) && T("5 โมง").Count == 0 && T("12.09.2026").Count == 0,
            "booking: Thai times of day; a bare 5 โมง is ambiguous and not read; a date's day and month are not a time");
        check(BookingVerification.TimeOf("08:30") == 510 && BookingVerification.TimeOf("8 โมง") is null,
            "booking: a time value must itself be a clock time");
        check(BookingVerification.Numbers("20,000 kg").SetEquals(["20000"]) && BookingVerification.Numbers("1.50 ton").SetEquals(["1.5"])
            && BookingVerification.Supports("weight", "20000", "น้ำหนัก 20,000 kg", Today) && !BookingVerification.Supports("weight", "20000", "20 ตัน", Today),
            "booking: numbers compare without separators or units — 20 ตัน is not 20000, and is not converted");
        check(BookingVerification.Key("A.C.N. Transport") == "acntransport" && BookingVerification.Key("ลาดกระบัง") == "ลาดกระบัง",
            "booking: names compare by letters, digits and Thai marks, case folded");

        /* ---------------- the reader's answer ---------------- */
        var mailJson = """{"isBooking":true,"category":"export","categoryQuote":"ส่งออก","fields":{"customer":{"value":"BASF","quote":"BASF"},"date":{"value":"29/09/2026"}}}""";
        var parsed = BookingTextReader.Parse(mailJson, null);
        check(parsed is { IsBooking: true, Category: "EXPORT", CategoryQuote: "ส่งออก" } && parsed.Fields["customer"] == new BookingProposal("BASF", "BASF")
            && parsed.Fields["date"] == new BookingProposal("29/09/2026", ""),
            "booking: the reader's answer is read field by field; a missing quote is an empty one, left for verification to refuse");
        check(BookingTextReader.Parse("""{"isBooking":true,"category":"SHIPPING","categoryQuote":"","fields":{}}""", null).Category == "NONE"
            && BookingTextReader.Parse("""{"isBooking":"yes","category":"IMPORT","categoryQuote":"","fields":{}}""", null).IsBooking == false
            && BookingTextReader.Parse("""{"fields":{}}""", "delivery") is { IsBooking: true, Category: "DELIVERY" },
            "booking: a category outside the list is NONE; isBooking must be a real true; a paste is a booking in the form's category");
        using (var schema = JsonDocument.Parse(BookingTextReader.Schema(BookingVerification.Fields["EXPORT"], classify: false)))
        {
            var fields = schema.RootElement.GetProperty("properties").GetProperty("fields");
            check(fields.GetProperty("required").GetArrayLength() == BookingVerification.Fields["EXPORT"].Length
                && fields.GetProperty("additionalProperties").GetBoolean() == false
                && fields.GetProperty("properties").GetProperty("closingDate").GetProperty("required").EnumerateArray().Select(one => one.GetString()).SequenceEqual(["value", "quote"]),
                "booking: every field is required as a {value, quote} pair and nothing else may be returned");
        }
        using (var schema = JsonDocument.Parse(BookingTextReader.Schema(["customer"], classify: true)))
            check(schema.RootElement.GetProperty("required").EnumerateArray().Select(one => one.GetString()).SequenceEqual(["isBooking", "category", "categoryQuote", "fields"]),
                "booking: for mail the model must also say whether it is a booking, and which kind, with the words");

        /* ---------------- the audit's one system identity ---------------- */
        AiExecutionEvent System(string @event, string? tool, string? view, string agent = BookingAgent.Id) => new(Guid.NewGuid().ToString("N"),
            AiAuditRules.SystemUser, AiAuditRules.SystemRole, agent, @event, tool, @event.EndsWith("started") ? "running" : "succeeded", Now,
            Scope: new(true, null), Model: "gpt-4.1", ToolCallId: tool is null ? null : Guid.NewGuid().ToString("N"), View: view, Limit: tool is null ? null : 1,
            Step: @event == "run_started" ? null : 1);
        bool Accepted(AiExecutionEvent e) { try { AiAuditRules.From(e); return true; } catch (ArgumentException) { return false; } }
        check(Accepted(System("run_started", null, null)) && Accepted(System("tool_started", BookingAgent.Tool, "mail")),
            "booking: the mail pass's run is recorded under the system identity");
        check(!Accepted(System("tool_started", BookingAgent.Tool, "import")) && !Accepted(System("run_started", null, null, "data-agent"))
            && !Accepted(System("tool_started", "query_kpi", "kpi", "data-agent"))
            && !Accepted(System("run_started", null, null) with { UserId = "someone" })
            && !Accepted(System("run_started", null, null) with { Role = Roles.Admin, UserId = AiAuditRules.SystemUser } with { AgentId = "sre-agent" }),
            "booking: that identity is the booking pass on mail and nothing else — no other agent, tool or view, and no one else may claim its role");
        check(!Roles.All.Any(role => role.Name == AiAuditRules.SystemRole), "booking: System is not a role an account can hold");
        var person = System("tool_started", BookingAgent.Tool, "export") with { UserId = Operator.UserId, Role = Operator.Role, Scope = new(false, "OP-B1") };
        check(Accepted(person) && !Accepted(person with { Role = Roles.Subcontractor }),
            "booking: a person's pasted read is recorded under their own name; a carrier's is not");

        /* ---------------- the registry ---------------- */
        var agents = new AgentRegistry();
        var booking = agents.Find(BookingAgent.Id)!;
        check(booking is { RequiredCapability: Capability.EditOwnJobs, DefaultShadow: true, MaxAutonomy: AiAutonomy.Recommend }
            && booking.AllowedTools.Count == 0 && booking.Pages.Count == 0 && !AgentRegistry.Enabled(booking, new AiOptions())
            && AgentRegistry.Enabled(booking, new AiOptions { BookingAgentEnabled = true }),
            "booking: the agent recommends at most, starts in shadow, offers nothing in chat, and is off without its own flag");
        check(AgentScanner.Agents.Contains(BookingAgent.Id)
            && !AgentScanner.Switched(booking, new AiOptions { Enabled = true, BookingAgentEnabled = true }, AgentGovernance.Default(booking))
            && AgentScanner.Switched(booking, new AiOptions { Enabled = true, BookingAgentEnabled = true, BookingMailEnabled = true }, AgentGovernance.Default(booking)),
            "booking: the pasted read's flag alone does not start the pass over mail — that has its own switch");

        /* ---------------- the pasted-text read under its controls ---------------- */
        var reader = new FixtureBookingReader();
        var reads = new OperationsTestAudit();
        using var limiter = new AiRunLimiter();
        var on = new AiOptions { Enabled = true, BookingAgentEnabled = true };
        BookingDraftService Service(AiOptions ai) => new(reader, reads, limiter, new DefaultGovernance(), new FixedCustomers(Customers),
            Options.Create(ai), new OperationsClock(Now), Options.Create(new OpenAiOptions { Model = "gpt-4.1" }));
        check((await Service(new AiOptions { Enabled = true }).DraftAsync(Operator, "IMPORT", Sample, "corr-b", default)).Status == 503 && reader.Calls == 0,
            "booking: with its flag off nothing is read");
        var pasted = await Service(on).DraftAsync(Operator, "import", Sample, "corr-b-1", default);
        check(pasted is { Status: 200, Draft: { Status: BookingDraft.Complete } } && pasted.Draft.Verified["customer"] == "BASF"
            && !pasted.Draft.Verified.ContainsKey("jobCode") && reader.Calls == 1 && reader.LastCategory == "IMPORT",
            "booking: a pasted booking comes back as the verified fields, the fabricated one left out");
        var trail = reads.Entries;
        check(trail.Select(e => e.Event).SequenceEqual(["run_started", "tool_started", "tool_completed", "run_completed"])
            && trail.All(e => e.AgentId == BookingAgent.Id && e.UserId == Operator.UserId && e.CorrelationId == "corr-b-1")
            && trail[1] is { Tool: BookingAgent.Tool, View: "import", Limit: 1 } && trail[3].SourceKeys!.SequenceEqual(["paste:1"])
            && trail.All(Accepted),
            "booking: the read is a run in the audit — the agent, its tool, the category, one key — and the strict sink takes every event");
        check(!JsonSerializer.Serialize(trail).Contains("BASF") && !JsonSerializer.Serialize(trail).Contains("TEMU"),
            "booking: no text, value or quote reaches the audit");
        check((await Service(on).DraftAsync(Carrier, "IMPORT", Sample, "", default)).Status == 403
            && (await Service(on).DraftAsync(Operator, "IMPORT", "  ", "", default)).Status == 400
            && (await Service(on).DraftAsync(Operator, "IMPORT", new string('x', BookingTextReader.MaxText + 1), "", default)).Status == 413
            && reader.Calls == 1,
            "booking: a carrier, an empty paste and an overlong one are refused before the model is called");
        reads.IsReady = false;
        check((await Service(on).DraftAsync(Operator, "IMPORT", Sample, "", default)).Status == 503 && reader.Calls == 1, "booking: with the audit not ready nothing is read");
        reads.IsReady = true;
        reads.FailAt = "tool_completed";
        var withheld = await Service(on).DraftAsync(Operator, "IMPORT", Sample, "", default);
        check(withheld is { Status: 503, Draft: null } && reader.Calls == 2, "booking: fields the audit could not record are withheld");
        reads.FailAt = null;
        reader.Busy = true;
        check((await Service(on).DraftAsync(Operator, "IMPORT", Sample, "", default)).Status == 429 && reads.Entries[^1].Status == "provider_busy",
            "booking: a busy provider is recorded as such");
        reader.Busy = false;

        /* ---------------- mail drafts as the decision log keeps them ---------------- */
        var known = agents.All.Select(agent => agent.Id).ToList();
        var mailDraft = BookingMailPass.Draft(41, "ขอรถ BASF", "Somchai", "somchai@basf.test", Now, new BookingReading(true, "IMPORT", "รับตู้", Good), draft);
        check(AgentResultRules.Problems(mailDraft, known).Count == 0 && mailDraft is { EntityType: "email", EntityId: "41", DecisionType: BookingAgent.DecisionType }
            && mailDraft.Facts.Count(fact => fact.Source!.StartsWith("email:41.text#")) == 8
            && mailDraft.Facts.Single(fact => fact.Source == "email:41.text#customer").Text == "BASF"
            && mailDraft.Observations.Any(note => note.Source == "quote#customer") && mailDraft.RuleReferences.Contains("Category:IMPORT"),
            "booking: a mail draft passes the decision contract — the accepted fields as facts sourced to the mail, their words beside them, the category");
        var notOne = BookingMailPass.NotABooking(42, "Newsletter", "news@x.test", Now);
        check(AgentResultRules.Problems(notOne, known).Count == 0 && notOne.DecisionType == "not_booking",
            "booking: a message that is not a booking is recorded as read, within the contract");

        if (sql) await SqlAsync(check, agents, reader);
    }

    private static async Task SqlAsync(Action<bool, string> check, AgentRegistry agents, FixtureBookingReader reader)
    {
        var database = "SCMOS_AI_BOOKING_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var setup = new ScmosDbContext(options);
        await setup.Database.EnsureCreatedAsync();
        try
        {
            var clock = new MovableClock(Now);
            var mailbox = new Mailbox { Address = "ops@test.invalid", IsActive = true, CreatedAt = Now, UpdatedAt = Now };
            setup.Mailboxes.Add(mailbox);
            setup.OperationJobs.Add(new OperationJob
            {
                Key = "B-JOB", Cat = "IMPORT", Owner = "Operator", OwnerId = "OP-B1", WorkDate = "29/09/2026", Customer = "BASF",
                Trucker = "", JobCode = "J-B", Status = "Waiting Truck", Data = new JsonObject { ["key"] = "B-JOB" }.ToJsonString(), UpdatedBy = "test", UpdatedAt = Now,
            });
            await setup.SaveChangesAsync();
            Email Mail(string id, string subject, string body, double hoursAgo, string status = MailProcessing.Processed) => new()
            {
                MailboxId = mailbox.Id, GraphMessageId = id, Subject = subject, BodyText = body, FromAddress = "cs@basf.test", FromName = "BASF CS",
                ReceivedAt = Now.AddHours(-hoursAgo), SentAt = Now.AddHours(-hoursAgo), ProcessingStatus = status,
            };
            var booking = Mail("m1", "ขอรถ 29/09", Sample, 1);
            var news = Mail("m2", "Newsletter", "Our September newsletter", 2);
            var placed = Mail("m3", "Re: TEMU5246902", "ขอรถ ตามนี้ " + Sample, 1);
            var old = Mail("m4", "ขอรถ เก่า", Sample, 72);
            var waiting = Mail("m5", "ขอรถ ยังไม่ประมวลผล", Sample, 1, MailProcessing.Received);
            setup.Emails.AddRange(booking, news, placed, old, waiting);
            await setup.SaveChangesAsync();
            setup.EmailJobLinks.Add(new EmailJobLink { EmailId = placed.Id, JobKey = "B-JOB", MatchedOn = "CONTAINER", MatchedValue = "TEMU5246902",
                Confidence = 0.8, Status = MailLink.Suggested, CreatedAt = Now });
            await setup.SaveChangesAsync();

            var audit = new OperationsTestAudit();
            var ai = new AiOptions { Enabled = true, BookingAgentEnabled = true, BookingMailEnabled = true };
            async Task<ScanSummary> Pass(AiOptions? with = null)
            {
                await using var db = new ScmosDbContext(options);
                var log = new AiDecisionLog(db, agents, new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance), clock);
                using var limiter = new AiRunLimiter();
                var pass = new BookingMailPass(db, reader, audit, limiter, log, Options.Create(with ?? ai), clock, NullLogger<BookingMailPass>.Instance,
                    Options.Create(new OpenAiOptions { Model = "gpt-4.1" }));
                return await pass.PassAsync(agents.Find(BookingAgent.Id)!, true, AiAutonomy.Recommend, Customers, default);
            }
            async Task<List<AiDecision>> Decisions()
            {
                await using var db = new ScmosDbContext(options);
                return await db.AiDecisions.AsNoTracking().Where(one => one.AgentId == BookingAgent.Id).OrderBy(one => one.Id).ToListAsync();
            }

            var before = reader.Calls;
            var first = await Pass();
            var rows = await Decisions();
            check(first is { Code: "ok", Created: 1 } && reader.Calls - before == 2 && rows.Count == 2
                && rows.Single(one => one.EntityId == booking.Id.ToString()) is { Status: AiDecisionLog.Open, DecisionType: BookingAgent.DecisionType, EntityType: "email", OwnerId: "", Shadow: true }
                && rows.Single(one => one.EntityId == news.Id.ToString()) is { Status: AiDecisionLog.Resolved, DecisionType: "not_booking", DecidedBy: "system" },
                "booking SQL: the unplaced booking becomes an open draft; the newsletter is read once and closed; the linked, the old and the unprocessed are not read");
            var draftRow = rows.Single(one => one.EntityId == booking.Id.ToString());
            var view = AiDecisionLog.View(draftRow);
            check(AiAuditRules.Id(draftRow.RunId) && audit.Entries.Any(e => e.RunId == draftRow.RunId && e.SourceKeys?.SequenceEqual([$"mail:{booking.Id}"]) == true)
                && view.Findings.Facts.Single(fact => fact.Source == $"email:{booking.Id}.text#customer").Text == "BASF",
                "booking SQL: the draft names the model run that read it, and that run's only evidence is the message's id");
            check(audit.Entries.All(e => e.UserId == AiAuditRules.SystemUser && e.Role == AiAuditRules.SystemRole) && audit.Entries.All(e =>
                { try { AiAuditRules.From(e); return true; } catch (ArgumentException) { return false; } }),
                "booking SQL: the pass is audited under the system identity, every event one the strict sink accepts");

            clock.Advance(TimeSpan.FromMinutes(15));
            var again = await Pass();
            check(again is { Code: "ok", Jobs: 0 } && reader.Calls - before == 2 && (await Decisions()).Count == 2,
                "booking SQL: a message read once is never read — or paid for — again");

            await using (var db = new ScmosDbContext(options))
            {
                db.EmailJobLinks.Add(new EmailJobLink { EmailId = booking.Id, JobKey = "B-JOB", MatchedOn = "MANUAL", MatchedValue = "",
                    Confidence = 1, Status = MailLink.Confirmed, ConfirmedBy = "op@test.invalid", ConfirmedAt = Now, CreatedAt = Now });
                await db.SaveChangesAsync();
            }
            clock.Advance(TimeSpan.FromMinutes(15));
            var linked = await Pass();
            check(linked.Resolved == 1 && (await Decisions()).Single(one => one.Id == draftRow.Id) is
                { Status: AiDecisionLog.Resolved, HumanChoice: "B-JOB", DecidedBy: "system" },
                "booking SQL: once a person links the mail to a job, the draft is resolved with that job");

            await using (var db = new ScmosDbContext(options))
            {
                db.Emails.AddRange(Mail("m6", "ขอรถ อีกงาน", Sample, 1), Mail("m7", "ขอรถ อีกงานสอง", Sample, 1));
                await db.SaveChangesAsync();
            }
            var capped = await Pass(new AiOptions { Enabled = true, BookingAgentEnabled = true, BookingMailEnabled = true, BookingMailPerPass = 1 });
            check(capped.Jobs == 1 && reader.Calls - before == 3, "booking SQL: at most the configured number of messages a pass");
            reader.Busy = true;
            var busy = await Pass();
            check(busy.Code == "provider_busy" && (await Decisions()).Count == 3, "booking SQL: a busy provider ends the pass and the message waits, unrecorded");
            reader.Busy = false;
            reader.IsConfigured = false;
            check((await Pass()).Code == "not_configured", "booking SQL: without a model configured the pass reads nothing");
            reader.IsConfigured = true;
        }
        finally
        {
            if (!database.StartsWith("SCMOS_AI_BOOKING_TEST_", StringComparison.Ordinal)
                || setup.Database.GetDbConnection().DataSource != "(localdb)\\MSSQLLocalDB") throw new InvalidOperationException("Unsafe cleanup target");
            await setup.Database.EnsureDeletedAsync();
        }
    }
}

/// <summary>A model that reads a booking in the sample's words, and says a text without "ขอรถ" is none.</summary>
sealed class FixtureBookingReader : IBookingTextReader
{
    public bool IsConfigured { get; set; } = true;
    public bool Configured => IsConfigured;
    public bool Busy { get; set; }
    public int Calls { get; private set; }
    public string? LastCategory { get; private set; }

    public Task<BookingReadResult> ReadAsync(string? category, string text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Calls++;
        LastCategory = category;
        if (Busy) return Task.FromResult(new BookingReadResult(null, "busy", StatusCodes.Status429TooManyRequests));
        if (!text.Contains("ขอรถ")) return Task.FromResult(new BookingReadResult(new BookingReading(false, "NONE", "", new Dictionary<string, BookingProposal>()), null, 200));
        var fields = new Dictionary<string, BookingProposal>(StringComparer.Ordinal)
        {
            ["customer"] = new("BASF", "BASF ขอรถรับตู้"), ["date"] = new("29/09/2026", "วันที่ 29/09/2026"), ["planTime"] = new("08:30", "เวลา 08:30 น."),
            ["type"] = new("1x40' HC", "ตู้ 1x40' HC"), ["destination"] = new("โรงงานระยอง", "ส่งโรงงานระยอง"), ["jobCode"] = new("J-999", "Job J-999"),
        };
        return Task.FromResult(new BookingReadResult(new BookingReading(true, category ?? "IMPORT", category is null ? "ขอรถรับตู้" : "", fields), null, 200));
    }
}

sealed class FixedCustomers(IReadOnlyCollection<string> names) : IKnownCustomers
{
    public Task<IReadOnlyCollection<string>> ListAsync(CancellationToken token) => Task.FromResult(names);
}
