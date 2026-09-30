namespace Scmos.Api.Rules;

/// <summary>One line of a carrier's invoice, as its form lists them.</summary>
/// <param name="Section">TRANSPORT — the transportation charge and what rides on it; REIMBURSEMENT — money the carrier laid out.</param>
public record InvoiceLineKind(string Code, string Section, string Number, string English, string Thai);

/// <summary>Who an invoice is made out to — NAME, ADDRESS and TAX ID on the form.</summary>
public record InvoiceParty(string Name, string Address, string TaxId);

/// <param name="Transport">TOTAL TRANSPORTATION CHARGE: 1.1–1.4.</param>
/// <param name="Reimbursement">TOTAL REIMBURSEMENT FROM THE RESERVE PAID.</param>
/// <param name="Withholding">Withholding tax, deducted: 1% of the transportation charge only.</param>
public record InvoiceTotals(decimal Transport, decimal Reimbursement, decimal Total, decimal Withholding, decimal Net);

/// <summary>
/// The carrier's invoice as the department's own paper one is laid out (30 Sep 2026, the department lead's
/// sample: a carrier's ใบแจ้งหนี้ to Leschaco). One job per invoice. Section 1 is the transportation charge
/// (1.1, priced from the carrier's Rate and editable) and three charges on it; section 2 is what the carrier
/// paid out and is reimbursed. None needs the department's approval or a receipt (the department's decision).
///
/// <para>
/// The arithmetic is <c>app/scmos/invoiceLines.ts</c>'s, and <c>tests/fixtures/billing-parity.json</c> holds both
/// to the sample's own figures: 7,879 + 100 + 963 = 8,942; 1% of the 7,879 is 78.79; 8,863.21 net.
/// </para>
/// </summary>
public static class InvoiceLines
{
    public const string Transport = "TRANSPORT";
    public const string Reimbursement = "REIMBURSEMENT";
    public const string TransportCharge = "TRANSPORT_CHARGE";

    /// <summary>Withholding tax on the transportation charge: one percent (the department's decision, 30 Sep 2026).</summary>
    public const decimal WithholdingRate = 0.01m;

    public static readonly InvoiceLineKind[] All =
    [
        new(TransportCharge, Transport, "1.1", "TRANSPORTATION CHARGE", "ค่าขนส่ง"),
        new("KNOCK_DOOR", Transport, "1.2", "KNOCK DOOR", "ค่าล่วงเวลา"),
        new("CHASSIS_DETENTION", Transport, "1.3", "CHASSIS DETENTION", "ค่าค้างหาง"),
        new("WAITING_TIME", Transport, "1.4", "WAITING TIME CHARGE", "ค่าเสียเวลา"),
        new("GATE_FEE", Reimbursement, "", "GATE FEE", "ค่าผ่านท่า"),
        new("GATE_CHARGE", Reimbursement, "", "GATE CHARGE", "ค่าบริการประตูท่า"),
        new("CLEANING_CHARGE", Reimbursement, "", "CLEANING CHARGE", "ค่าล้างตู้"),
        new("REPAIR_CHARGE", Reimbursement, "", "REPAIR CHARGE", "ค่าซ่อมตู้"),
        new("LIFT_ON", Reimbursement, "", "LIFT ON", "ค่ายกตู้ขึ้น"),
        new("LIFT_OFF", Reimbursement, "", "LIFT OFF", "ค่ายกตู้ลง"),
    ];

    /// <summary>Leschaco as the carriers' invoices address it — the sample's own NAME, ADDRESS and TAX ID boxes.</summary>
    public static readonly InvoiceParty BillTo = new("Leschaco (Thailand) Ltd. (Head Office)",
        "3195/14 9th floor, Vibulthani Tower, Rama 4 road, Klongton, Klongtoey, Bangkok 10110", "0105542055418");

    public static InvoiceLineKind? Of(string code) => All.FirstOrDefault(kind => kind.Code == code);

    /// <summary>A line's amount: quantity × unit price, to the satang.</summary>
    public static decimal Amount(decimal quantity, decimal unitPrice) =>
        decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);

    public static InvoiceTotals Totals(IEnumerable<(string Code, decimal Quantity, decimal UnitPrice)> lines)
    {
        decimal transport = 0, reimbursement = 0;
        foreach (var (code, quantity, unitPrice) in lines)
        {
            var amount = Amount(quantity, unitPrice);
            if (Of(code)?.Section == Reimbursement) reimbursement += amount;
            else transport += amount;
        }
        var withholding = decimal.Round(transport * WithholdingRate, 2, MidpointRounding.AwayFromZero);
        var total = transport + reimbursement;
        return new(transport, reimbursement, total, withholding, total - withholding);
    }
}
