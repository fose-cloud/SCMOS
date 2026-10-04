using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Endpoints;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// The Supplier Register's audit rows (4 Oct 2026): an edit is written down field by field, each with what it held
/// before and after — not as the fixed label over an empty field and empty values that two production ABS changes left
/// that day. Status, alias, evaluation, merge and removal say what they changed too. Through the real routes on a
/// loopback host over a throwaway LocalDB (--write-local-db).
/// </summary>
static class SupplierAuditChecks
{
    private static readonly AppUser Supervisor = new("sup-audit-sv", "sv@test.invalid", "Supervisor", Roles.Supervisor, "SV-S1", "test", true);

    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        if (sql) await SqlAsync(check);
    }

    private static async Task SqlAsync(Action<bool, string> check)
    {
        // The connection is compiled test-only LocalDB. Never read settings or accept a server argument.
        var database = "SCMOS_SUPPLIER_AUDIT_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var setup = new ScmosDbContext(options);
        await setup.Database.EnsureCreatedAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            Supplier Company(string code, string name, string absNo = "") => new()
                { Code = code, Name = name, AbsNo = absNo, Status = "approved", CreatedAt = now, UpdatedAt = now };
            var alpha = Company("ALPHA", "ALPHA TRANSPORT", "252015");
            var bravo = Company("BRAVO", "BRAVO LOGISTICS");
            var spare = Company("SPARE", "SPARE CO");
            var twin = Company("ALPHAX", "ALPHA TRANSPORT CO., LTD.");
            setup.Suppliers.AddRange(alpha, bravo, spare, twin);
            await setup.SaveChangesAsync();
            setup.SupplierAliases.Add(new SupplierAlias { SupplierId = bravo.Id, Alias = "BRV", Source = "manual", Confirmed = true });
            await setup.SaveChangesAsync();

            var builder = WebApplication.CreateBuilder();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddScoped(_ => new ScmosDbContext(options));
            builder.Services.AddMemoryCache();
            builder.Services.AddScoped<JobRegisterCache>();
            builder.Services.AddScoped<CarrierDirectory>();
            builder.Services.AddScoped<KpiEngine>();
            builder.Services.AddScoped<SupplierService>();
            builder.Services.AddScoped<IncidentService>();
            builder.Services.AddScoped<RateService>();
            builder.Services.AddScoped<AuditService>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<IUserAccessor>(new TestUsers { User = Supervisor });
            await using var app = builder.Build();
            app.MapSuppliers();
            await app.StartAsync();
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                async Task<(int Status, string Message)> Send(HttpResponseMessage response)
                {
                    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                    return ((int)response.StatusCode,
                        json.TryGetProperty("message", out var said) || json.TryGetProperty("error", out said) ? said.GetString() ?? "" : "");
                }
                async Task<(int Status, string Message)> Post(string path, object body) =>
                    await Send(await http.PostAsJsonAsync("/api/suppliers" + path, body));
                long seen = 0;
                async Task<List<AuditEvent>> Written()
                {
                    await using var db = new ScmosDbContext(options);
                    var rows = await db.AuditEvents.AsNoTracking().Where(row => row.Id > seen).OrderBy(row => row.Id).ToListAsync();
                    if (rows.Count > 0) seen = rows[^1].Id;
                    return rows;
                }
                bool Row(AuditEvent row, int supplier, string field, string before, string after) =>
                    row.Entity == "supplier" && row.EntityId == supplier.ToString()
                    && row.Field == field && row.OldValue == before && row.NewValue == after;

                var abs = await Post($"/{alpha.Id}/edit", new { absNo = " 132143 ", reason = "ABS ตามรายชื่อ ASL ปี 2026" });
                var absRows = await Written();
                check(abs.Status == 200 && abs.Message == "แก้ไข ALPHA TRANSPORT แล้ว — ABS No 252015 → 132143"
                    && absRows.Count == 1 && Row(absRows[0], alpha.Id, "ABS No", "252015", "132143")
                    && absRows[0].Action == AuditActions.Update && absRows[0].EntityLabel == "ข้อมูลบริษัท"
                    && absRows[0].Reason == "ABS ตามรายชื่อ ASL ปี 2026" && absRows[0].Who == Supervisor.Signature,
                    "supplier audit: an ABS change is one row on the company — field ABS No, the number it held and the number it holds, with the reason");

                var taken = await Post($"/{bravo.Id}/edit", new { absNo = "132143", reason = "test" });
                check(taken.Status == 400 && (await Written()).Count == 0,
                    "supplier audit: an edit the service refuses writes no row");

                var several = await Post($"/{alpha.Id}/edit", new { absNo = "", telephone = "038-000-000", gpsEquipped = true, reason = "ตรวจทะเบียน" });
                var severalRows = await Written();
                check(several.Status == 200 && several.Message == "แก้ไข ALPHA TRANSPORT แล้ว — ABS No 132143 → (ว่าง) · โทรศัพท์ → 038-000-000 · มี GPS: ใช่"
                    && severalRows.Count == 3 && Row(severalRows[0], alpha.Id, "ABS No", "132143", "")
                    && Row(severalRows[1], alpha.Id, "โทรศัพท์", "", "038-000-000") && Row(severalRows[2], alpha.Id, "มี GPS", "ไม่", "ใช่")
                    && severalRows.All(row => row.Reason == "ตรวจทะเบียน"),
                    "supplier audit: an edit of three fields is three rows, each with its own value before and after — an emptied field included");

                var nothing = await Post($"/{alpha.Id}/edit", new { absNo = "", reason = "" });
                var nothingRows = await Written();
                check(nothing.Status == 200 && nothingRows.Count == 1 && Row(nothingRows[0], alpha.Id, "", "", "ไม่มีอะไรเปลี่ยน"),
                    "supplier audit: an edit that changed nothing is one row saying so, not one claiming a new value");

                var status = await Post($"/{alpha.Id}/status", new { status = "suspended", reason = "รอผลตรวจ" });
                var statusRows = await Written();
                check(status.Status == 200 && statusRows.Count == 1 && Row(statusRows[0], alpha.Id, "สถานะ", "approved", "suspended")
                    && statusRows[0].EntityLabel == "สถานะการอนุมัติ",
                    "supplier audit: a status change keeps the status it replaced");

                var alias = await Post($"/{alpha.Id}/alias", new { alias = " brv ", reason = "ชื่อย่อในแผน" });
                var aliasRows = await Written();
                check(alias.Status == 200 && aliasRows.Count == 2
                    && Row(aliasRows[0], bravo.Id, "ชื่อที่ผูก", "BRV", "") && Row(aliasRows[1], alpha.Id, "ชื่อที่ผูก", "", "BRV"),
                    "supplier audit: a spelling taken from another company is written down against both — the one that lost it and the one that has it");

                var first = await Post($"/{alpha.Id}/evaluate", new { period = "2026", safety = 90, documents = 80, note = "รอบแรก" });
                var firstRows = await Written();
                var again = await Post($"/{alpha.Id}/evaluate", new { period = "2026", safety = 60, documents = 70, note = "แก้คะแนน" });
                var againRows = await Written();
                check(first.Status == 200 && again.Status == 200 && firstRows.Count == 1 && againRows.Count == 1
                    && firstRows[0].Field == "ผลประเมิน" && firstRows[0].OldValue == "" && firstRows[0].NewValue.Contains("ความปลอดภัย 90")
                    && againRows[0].OldValue == firstRows[0].NewValue && againRows[0].NewValue.Contains("ความปลอดภัย 60")
                    && againRows[0].NewValue.Contains("เอกสาร 70"),
                    "supplier audit: re-scoring a period keeps the scores it overwrote");

                var merged = await Post("/merge", new { keepId = alpha.Id, foldId = twin.Id, reason = "รายการซ้ำ" });
                var mergedRows = await Written();
                check(merged.Status == 200 && mergedRows.Count == 2
                    && Row(mergedRows[0], alpha.Id, "รวมรายการซ้ำ", $"ALPHAX · ALPHA TRANSPORT CO., LTD. · #{twin.Id}", $"ALPHA · ALPHA TRANSPORT · #{alpha.Id}")
                    && Row(mergedRows[1], alpha.Id, "ชื่อ", "ALPHA TRANSPORT", "ALPHA TRANSPORT CO., LTD."),
                    "supplier audit: a merge names the row it folded, and the rename it made to the row that survives");

                var removed = await Send(await http.DeleteAsync($"/api/suppliers/{spare.Id}?reason=" + Uri.EscapeDataString("พิมพ์ผิด")));
                var removedRows = await Written();
                check(removed.Status == 200 && removedRows.Count == 2
                    && Row(removedRows[0], spare.Id, "รหัส", "SPARE", "") && Row(removedRows[1], spare.Id, "ชื่อ", "SPARE CO", ""),
                    "supplier audit: a removed company's code and name survive in the trail, the only place its id still means anything");
            }
            finally { await app.StopAsync(); }
        }
        finally
        {
            // Delete only this run's explicitly named local scratch database, never application data.
            if (!database.StartsWith("SCMOS_SUPPLIER_AUDIT_TEST_", StringComparison.Ordinal)
                || setup.Database.GetDbConnection().DataSource != "(localdb)\\MSSQLLocalDB") throw new InvalidOperationException("Unsafe cleanup target");
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
