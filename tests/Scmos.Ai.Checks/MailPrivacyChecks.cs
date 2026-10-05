using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Endpoints;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// Personal mailboxes (4 Oct 2026): a message is read only from a listed sender, and what is kept is seen by the owner,
/// Supervisor and above, and whoever owns a job it is linked to — in the Communication Center, its attachments, the AI's
/// findings and the AI's chat. Through the real routes on a loopback host over a throwaway LocalDB (--write-local-db).
/// </summary>
static class MailPrivacyChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "booking@kwe.co.th" };
        check(MailSenders.Reads("", "anyone@x.com", allowed) && MailSenders.Reads("OP-1", " Booking@KWE.co.th ", allowed)
            && !MailSenders.Reads("OP-1", "sales@kwe.co.th", allowed) && !MailSenders.Reads("OP-1", "", allowed)
            && !MailSenders.Reads("OP-1", "booking@kwe.co.th", new HashSet<string>()),
            "senders: a shared mailbox reads everything; a personal one only a listed address, compared without case — nobody listed reads nothing");
        check(MailSenders.IsAddress("booking@kwe.co.th") && !MailSenders.IsAddress("kwe.co.th") && !MailSenders.IsAddress("@kwe.co.th")
            && !MailSenders.IsAddress("*@kwe.co.th") && !MailSenders.IsAddress("a@b") && !MailSenders.IsAddress("a b@kwe.co.th")
            && !MailSenders.IsAddress("a@kwe.co.th,b@kwe.co.th"),
            "senders: a full address only — never a domain, a pattern or a list (the user's decision)");

        if (sql) await SqlAsync(check);
    }

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_MAIL_PRIVACY_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var db = new ScmosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            StaffMember Person(string id, string email, string role) => new() { Id = id, Email = email, Name = id, Role = role, Active = true };
            db.Staff.AddRange(Person("OP-1", "op1@leschaco.com", Roles.Operation), Person("OP-2", "op2@leschaco.com", Roles.Operation),
                Person("SV-1", "sv1@leschaco.com", Roles.Supervisor));
            Mailbox Box(string address, string owner) => new() { Address = address, DisplayName = address, OwnerOperatorId = owner, IsActive = true, CreatedAt = now, UpdatedAt = now };
            var shared = Box("ops@leschaco.com", "");
            var one = Box("op1@leschaco.com", "OP-1");
            var two = Box("op2@leschaco.com", "OP-2");
            db.Mailboxes.AddRange(shared, one, two);
            OperationJob Job(string key, string owner) => new() { Key = key, OwnerId = owner, Status = JobStatus.Completed, Data = "{}", UpdatedAt = now };
            db.OperationJobs.AddRange(Job("J-OP2", "OP-2"), Job("J-OP1", "OP-1"));
            await db.SaveChangesAsync();

            Email Mail(Mailbox box, string subject) => new()
            {
                MailboxId = box.Id, GraphMessageId = Guid.NewGuid().ToString("N"), Subject = subject, FromAddress = "booking@kwe.co.th",
                BodyText = subject, ReceivedAt = now, SentAt = now, CreatedAt = now, ProcessingStatus = MailProcessing.Processed,
            };
            var s = Mail(shared, "shared");
            var p1 = Mail(one, "op1-own");
            var p2 = Mail(two, "op2-own");
            var p3 = Mail(two, "op2-rejected-for-op1");
            db.Emails.AddRange(s, p1, p2, p3);
            await db.SaveChangesAsync();
            db.EmailJobLinks.AddRange(
                new EmailJobLink { EmailId = p1.Id, JobKey = "J-OP2", Status = MailLink.Suggested, Confidence = 0.8, MatchedOn = "container", CreatedAt = now },
                new EmailJobLink { EmailId = p3.Id, JobKey = "J-OP1", Status = MailLink.Rejected, Confidence = 0.8, MatchedOn = "container", CreatedAt = now },
                new EmailJobLink { EmailId = s.Id, JobKey = "J-OP1", Status = MailLink.Confirmed, Confidence = 1, MatchedOn = "job", CreatedAt = now });
            var attachment = new StoredDocument { Scope = MailAttachments.Scope, Folder = MailAttachments.Folder, FileName = "booking.pdf", ObjectKey = "mail/x" };
            db.Documents.Add(attachment);
            await db.SaveChangesAsync();
            db.EmailAttachments.Add(new EmailAttachment { EmailId = p2.Id, GraphAttachmentId = "a1", FileName = "booking.pdf", StoredDocumentId = attachment.Id });
            await db.SaveChangesAsync();

            AppUser User(string operatorId, string role) => new(operatorId.ToLowerInvariant(), operatorId.ToLowerInvariant() + "@test.invalid", operatorId, role, operatorId, "test", true);
            var op1 = User("OP-1", Roles.Operation);
            var op2 = User("OP-2", Roles.Operation);
            var sv1 = User("SV-1", Roles.Supervisor);
            var admin = User("AD-1", Roles.Admin);
            var nobody = new AppUser("cs", "cs@test.invalid", "CS", Roles.CustomerService, "", "test", true);
            async Task<List<string>> Seen(AppUser user) =>
                await db.Emails.AsNoTracking().VisibleTo(db, user).OrderBy(mail => mail.Subject).Select(mail => mail.Subject).ToListAsync();
            check((await Seen(op1)).SequenceEqual(["op1-own", "shared"])
                && (await Seen(op2)).SequenceEqual(["op1-own", "op2-own", "op2-rejected-for-op1", "shared"])
                && (await Seen(sv1)).Count == 4 && (await Seen(nobody)).SequenceEqual(["shared"]),
                "visibility: shared mail for all; a personal mailbox for its owner, Supervisor+ and the owner of a job it is linked to — a rejected link opens nothing");

            check(await MailVisibility.HidesDocumentAsync(db, op1, attachment, default)
                && !await MailVisibility.HidesDocumentAsync(db, op2, attachment, default)
                && !await MailVisibility.HidesDocumentAsync(db, sv1, attachment, default)
                && (await MailVisibility.HiddenDocuments(db, op1).ToListAsync()).SequenceEqual([attachment.Id]),
                "attachments: a file that came on a message follows it — hidden from those who may not see the message");
            attachment.JobKey = "J-OP2";
            await db.SaveChangesAsync();
            check(!await MailVisibility.HidesDocumentAsync(db, op1, attachment, default) && !await MailVisibility.HiddenDocuments(db, op1).AnyAsync(),
                "attachments: once filed to a job it is that job's paperwork, seen as the job's documents are");

            AiDecision Decision(string entity, string owner) => new() { AgentId = "booking-agent", EntityType = entity, EntityId = "1", OwnerId = owner, Status = "OPEN" };
            var findings = new List<AiDecision> { Decision("email", "OP-2"), Decision("email", ""), Decision("job", "OP-2") }.AsQueryable();
            check(AiDecisionLog.WithoutOthersMail(findings, op1).Count() == 2 && AiDecisionLog.WithoutOthersMail(findings, op2).Count() == 3
                && AiDecisionLog.WithoutOthersMail(findings, sv1).Count() == 3,
                "AI findings: a booking draft from somebody's own mailbox is theirs and Supervisor+'s — not the rest of the team's");

            var source = new CommunicationSource(db);
            var quoted = await source.MailAsync(["J-OP1", "J-OP2"], 50, default);
            check(quoted.Count == 1 && quoted[0].EmailId == s.Id,
                "AI chat: the model is given shared-mailbox mail only — a personal mailbox's never reaches an answer");

            // The routes, as each person.
            var users = new TestUsers();
            var builder = WebApplication.CreateBuilder();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [GraphAuth.MailboxesKey] = "op1@leschaco.com, ops@leschaco.com" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddScoped(_ => new ScmosDbContext(options));
            builder.Services.AddScoped<AuditService>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddHttpClient();
            builder.Services.AddSingleton<GraphAuth>();
            builder.Services.AddSingleton<IUserAccessor>(users);
            await using var app = builder.Build();
            app.MapMail();
            await app.StartAsync();
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                async Task<(int Status, JsonElement Body)> Call(AppUser user, HttpMethod method, string path, object? body = null)
                {
                    users.User = user;
                    using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
                    using var response = await http.SendAsync(request);
                    var text = await response.Content.ReadAsStringAsync();
                    return ((int)response.StatusCode, text.Length == 0 ? default : JsonSerializer.Deserialize<JsonElement>(text));
                }
                int Count(JsonElement body) => body.GetProperty("messages").GetArrayLength();

                var listOp1 = await Call(op1, HttpMethod.Get, "/api/mail?view=ALL");
                var listSv = await Call(sv1, HttpMethod.Get, "/api/mail?view=ALL");
                var otherMail = await Call(op1, HttpMethod.Get, $"/api/mail/{p2.Id}");
                var ownMail = await Call(op1, HttpMethod.Get, $"/api/mail/{p1.Id}");
                check(Count(listOp1.Body) == 2 && listOp1.Body.GetProperty("total").GetInt32() == 2 && Count(listSv.Body) == 4
                    && otherMail.Status == 404 && ownMail.Status == 200 && ownMail.Body.GetProperty("personal").GetBoolean(),
                    "Communication Center: the list and a message obey the rule; a colleague's mail answers not found, not forbidden");

                var byOperation = await Call(op1, HttpMethod.Post, "/api/mail/senders", new { address = "booking@kwe.co.th" });
                var domain = await Call(sv1, HttpMethod.Post, "/api/mail/senders", new { address = "@kwe.co.th" });
                var added = await Call(sv1, HttpMethod.Post, "/api/mail/senders", new { address = " Booking@KWE.co.th ", note = "KWE desk" });
                var twice = await Call(sv1, HttpMethod.Post, "/api/mail/senders", new { address = "booking@kwe.co.th" });
                var listed = await Call(op1, HttpMethod.Get, "/api/mail/senders");
                var row = await db.MailAllowedSenders.AsNoTracking().SingleAsync();
                var removedByOp = await Call(op1, HttpMethod.Delete, $"/api/mail/senders/{row.Id}");
                var removed = await Call(sv1, HttpMethod.Delete, $"/api/mail/senders/{row.Id}");
                var trail = await db.AuditEvents.AsNoTracking().Where(one => one.Entity == "mail-sender").ToListAsync();
                check(byOperation.Status == 403 && domain.Status == 400 && added.Status == 200 && twice.Status == 409
                    && row.Address == "booking@kwe.co.th" && row.Note == "KWE desk"
                    && listed.Status == 200 && !listed.Body.GetProperty("canManage").GetBoolean()
                    && removedByOp.Status == 403 && removed.Status == 200 && !await db.MailAllowedSenders.AnyAsync()
                    && trail.Count == 2 && trail.All(one => one.Who == sv1.Signature),
                    "senders: Supervisor+ keep the list, a full address once each; Operation reads it; every change in the audit trail");

                var sharedForPerson = await Call(admin, HttpMethod.Put, "/api/mail/mailboxes", new { address = "op1@leschaco.com", owner = "", active = true });
                var unapproved = await Call(admin, HttpMethod.Put, "/api/mail/mailboxes", new { address = "op2@leschaco.com", owner = "OP-2", active = true });
                var bySupervisor = await Call(sv1, HttpMethod.Put, "/api/mail/mailboxes", new { address = "op1@leschaco.com", owner = "OP-1", active = true });
                users.Refusal = "Second-factor sign-in required";
                var single = await Call(admin, HttpMethod.Put, "/api/mail/mailboxes", new { address = "op1@leschaco.com", owner = "OP-1", active = false });
                users.Refusal = null;
                var declared = await Call(admin, HttpMethod.Put, "/api/mail/mailboxes", new { address = "op1@leschaco.com", owner = "OP-1", active = false });
                var boxes = await Call(admin, HttpMethod.Get, "/api/mail/mailboxes");
                var stored = await db.Mailboxes.AsNoTracking().SingleAsync(box => box.Address == "op1@leschaco.com");
                check(sharedForPerson.Status == 400 && sharedForPerson.Body.GetProperty("error").GetString()!.Contains("ส่วนตัว")
                    && unapproved.Status == 400 && bySupervisor.Status == 403 && single.Status == 403
                    && declared.Status == 200 && stored.OwnerOperatorId == "OP-1" && !stored.IsActive
                    && boxes.Status == 200 && boxes.Body.GetProperty("mailboxes").GetArrayLength() == 3
                    && await db.AuditEvents.AnyAsync(one => one.Entity == "mailbox" && one.NewValue.Contains("ส่วนตัว OP-1")),
                    "mailboxes: the Administrator, with the second factor, declares an approved mailbox — a person's own address can never be shared");

                // 5 Oct 2026: a job's owner attaches a message they can see to their own job; nothing else is theirs.
                var attachOwn = await Call(op1, HttpMethod.Post, $"/api/mail/{p1.Id}/decide", new { jobKey = "J-OP1", status = "CONFIRMED" });
                var attachOthers = await Call(op1, HttpMethod.Post, $"/api/mail/{p1.Id}/decide", new { jobKey = "J-OP2", status = "CONFIRMED" });
                var rejectOwn = await Call(op1, HttpMethod.Post, $"/api/mail/{p1.Id}/decide", new { jobKey = "J-OP1", status = "REJECTED" });
                var attachUnseen = await Call(op1, HttpMethod.Post, $"/api/mail/{p2.Id}/decide", new { jobKey = "J-OP1", status = "CONFIRMED" });
                var attachNoOperator = await Call(nobody, HttpMethod.Post, $"/api/mail/{s.Id}/decide", new { jobKey = "J-OP1", status = "CONFIRMED" });
                var bySupervisorAny = await Call(sv1, HttpMethod.Post, $"/api/mail/{p2.Id}/decide", new { jobKey = "J-OP1", status = "CONFIRMED" });
                var made = await db.EmailJobLinks.AsNoTracking().SingleAsync(one => one.EmailId == p1.Id && one.JobKey == "J-OP1");
                var untouched = await db.EmailJobLinks.AsNoTracking().SingleAsync(one => one.EmailId == p1.Id && one.JobKey == "J-OP2");
                check(attachOwn.Status == 200 && made is { Status: MailLink.Confirmed, MatchedOn: "PERSON" } && made.ConfirmedBy == op1.Signature
                    && attachOthers.Status == 403 && untouched.Status == MailLink.Suggested
                    && rejectOwn.Status == 403 && attachUnseen.Status == 403 && attachNoOperator.Status == 403
                    && bySupervisorAny.Status == 200
                    && await db.AuditEvents.AnyAsync(one => one.Entity == "email" && one.EntityId == p1.Id.ToString() && one.Who == op1.Signature),
                    "links: a job's owner attaches mail they can see to their own job — not another's, not unseen mail, never a rejection; Supervisor+ any");
            }
            finally { await app.StopAsync(); }
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
