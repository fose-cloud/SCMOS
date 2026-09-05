using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record QuoteSaveReceipt(long Id, int Number, string Date, int Count, int RouteCount = 1);
public record QuoteSaveOutcome(int Status, string Message, QuoteSaveReceipt? Receipt = null, bool Replayed = false);

/// <summary>All journeys, truck prices and the quotation's retry receipt commit together.</summary>
public class QuoteSheetService(ScmosDbContext db, QuoteCardService card,
    RateInquiryService inquiries, AuditService audit)
{
    private const string Entity = "quote-sheet-save";
    public async Task<QuoteSaveOutcome> SaveAsync(AppUser user, QuoteSaveBody body, CancellationToken token)
    {
        if (!Guid.TryParse(body.RequestId, out var requestId))
            return new(400, "คำขอบันทึกไม่ถูกต้อง กรุณาเปิดหน้าคำนวณใหม่");
        if (string.IsNullOrWhiteSpace(body.Customer) || body.Customer.Trim().Length > 200)
            return new(400, "ต้องระบุชื่อลูกค้าไม่เกิน 200 ตัวอักษร");
        if (body.Routes?.Any(route => route is null) == true)
            return new(400, "ข้อมูลเส้นทางไม่ครบถ้วน");
        var journeys = QuoteCalculation.Journeys(body);
        if (journeys.Count == 0 || journeys.Count > 20)
            return new(400, "ต้องมีเส้นทางอย่างน้อย 1 และไม่เกิน 20 เส้นทาง");
        foreach (var journey in journeys)
            if (string.IsNullOrWhiteSpace(journey.FromPlace) || journey.FromPlace.Trim().Length > 300 ||
                string.IsNullOrWhiteSpace(journey.ToPlace) || journey.ToPlace.Trim().Length > 300)
                return new(400, "ต้องระบุต้นทางและปลายทางของทุกเส้นทางไม่เกิน 300 ตัวอักษร");
        if (!body.Fcl && !body.Lcl && !body.Domestic)
            return new(400, "เลือก FCL, LCL หรือ Domestic อย่างน้อยหนึ่งอย่าง");

        // Scope retries to the authenticated identity, not a posted user name.
        var owner = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.UserId + "|" + user.Signature)))[..32];
        var key = owner + ":" + requestId.ToString("D");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body))));
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // An execution strategy may replay after a lost COMMIT response.
            // Reload the durable receipt before attempting another insertion.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            await RateWriteLock.TakeAsync(db, Entity + ":" + key, token);
            var previous = await db.AuditEvents.AsNoTracking()
                .FirstOrDefaultAsync(one => one.Entity == Entity && one.EntityId == key, token);
            if (previous is not null)
            {
                if (previous.OldValue != hash) return new QuoteSaveOutcome(409, "คำขอเดิมมีข้อมูลต่างกัน กรุณาเปลี่ยนข้อมูลแล้วบันทึกใหม่");
                var receipt = JsonSerializer.Deserialize<QuoteSaveReceipt>(previous.NewValue)
                    ?? throw new InvalidOperationException("Invalid quotation save receipt");
                return new QuoteSaveOutcome(200, $"รายการนี้บันทึกแล้ว · NO. {receipt.Number}", receipt, true);
            }

            var currentCard = await card.ReadAsync(token);
            var lanes = new List<RateInquiryService.LanePost>();
            var priceCount = 0;
            foreach (var (journey, index) in journeys.Select((one, at) => (one, at)))
            {
                var calculated = QuoteCalculation.Calculate(currentCard, journey);
                if (calculated.Error.Length > 0) return new QuoteSaveOutcome(400, $"เส้นทาง {index + 1}: {calculated.Error}");
                if (!QuoteCalculation.MatchesPreview(calculated, journey.ExpectedTotals))
                    return new QuoteSaveOutcome(409, "สูตรหรืออัตรามีการเปลี่ยนแปลง กรุณาตรวจราคาล่าสุดของทุกเส้นทางและกดบันทึกอีกครั้ง");
                lanes.Add(new(journey.FromPlace!.Trim(), journey.ToPlace!.Trim(), "", "", body.Fcl, body.Lcl,
                    calculated.Remark, calculated.Prices, body.Domestic));
                priceCount += calculated.Prices.Count;
            }

            var date = QuoteCalculation.DateAt(DateTimeOffset.UtcNow);
            var result = await inquiries.CreateAsync(user, new RateInquiryService.InquiryPost(
                date, body.Customer.Trim(), "", lanes), token);
            if (!result.Ok) return new QuoteSaveOutcome(400, result.Message);
            var saved = new QuoteSaveReceipt(result.Id, result.Number, date, priceCount, lanes.Count);
            // The receipt is deliberately small enough for the existing audit
            // field (400 chars), and is required, unlike best-effort UI audit.
            if (!await audit.RecordAsync(user, AuditActions.Register, Entity, key,
                $"Rate Calculator -> Rate Sheet · NO. {result.Number}", "save-receipt",
                hash, JsonSerializer.Serialize(saved), "ราคาขายรวมกำไรและรายการเพิ่มเติม", token))
                throw new InvalidOperationException("Quotation receipt could not be persisted; rolling back");
            await transaction.CommitAsync(token);
            return new QuoteSaveOutcome(200,
                $"บันทึกลงตารางอัตราแล้ว · NO. {saved.Number} · {saved.RouteCount} เส้นทาง · {saved.Count} ช่องราคา", saved);
        });
    }
}
