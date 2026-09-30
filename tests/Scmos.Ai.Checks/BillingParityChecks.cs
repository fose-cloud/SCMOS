using System.Text.Json;
using Scmos.Api.Rules;

/// <summary>
/// The server's half of tests/fixtures/billing-parity.json (30 Sep 2026): the diesel month average and the
/// invoice totals give the fixture's answers here, as tests/billingParity.test.mjs holds the browser's to it.
/// </summary>
static class BillingParityChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "tests", "fixtures", "billing-parity.json"))) root = root.Parent;
        check(root is not null, "billing parity: the shared fixture is found");
        if (root is null) return;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "tests", "fixtures", "billing-parity.json")));
        var diesel = fixture.RootElement.GetProperty("diesel");
        var changes = diesel.GetProperty("changes").EnumerateArray()
            .Select(one => (one.GetProperty("date").GetString()!, one.GetProperty("price").GetDouble())).ToList();
        var all = true;
        foreach (var one in diesel.GetProperty("cases").EnumerateArray())
        {
            var month = one.GetProperty("month").GetString()!;
            var until = one.GetProperty("until").GetString();
            var got = DieselMonth.AverageFor(DieselMonth.Expand(changes, month, string.IsNullOrEmpty(until) ? null : until), month);
            decimal? wanted = one.GetProperty("average").ValueKind == JsonValueKind.Null ? null : one.GetProperty("average").GetDecimal();
            all &= got.Average == wanted && got.Days == one.GetProperty("days").GetInt32() && got.Closed == one.GetProperty("closed").GetBoolean();
        }
        check(all, "billing parity: a month's diesel average is the fixture's — the same as the browser's");
        check(DieselMonth.ForJob(changes, "12/05/2025", null)?.Average == 36.05m && DieselMonth.ForJob(changes, "12/03/2025", null) is null
            && DieselMonth.ForJob(changes, "not a date", null) is null,
            "billing parity: a job is priced at its own month's average, and a month with no price gives none");

        var invoice = fixture.RootElement.GetProperty("invoice");
        var lines = invoice.GetProperty("lines").EnumerateArray().Select(one => (one.GetProperty("code").GetString()!,
            one.GetProperty("quantity").GetDecimal(), one.GetProperty("unitPrice").GetDecimal())).ToList();
        var totals = InvoiceLines.Totals(lines);
        var wantedTotals = invoice.GetProperty("totals");
        check(totals.Transport == wantedTotals.GetProperty("transport").GetDecimal()
            && totals.Reimbursement == wantedTotals.GetProperty("reimbursement").GetDecimal()
            && totals.Total == wantedTotals.GetProperty("total").GetDecimal()
            && totals.Withholding == wantedTotals.GetProperty("withholding").GetDecimal()
            && totals.Net == wantedTotals.GetProperty("net").GetDecimal(),
            "billing parity: the department's sample invoice totals as printed — 8,942.00 less 78.79 is 8,863.21");

        var vehicles = fixture.RootElement.GetProperty("vehicles").EnumerateArray()
            .All(one => RateMatch.VehicleForType(one.GetProperty("type").GetString()) == one.GetProperty("vehicle").GetString());
        check(vehicles, "billing parity: a job's container wording means the vehicle Booking reads — 1X40' is 40F, 1x6 WH is 6W");
        var scores = fixture.RootElement.GetProperty("laneScores").EnumerateArray().All(one =>
        {
            var destination = one.GetProperty("destination").GetString()!;
            var score = RateMatch.Score(one.GetProperty("customer").GetString()!,
                destination.Length > 0 ? destination : one.GetProperty("plant").GetString()!,
                one.GetProperty("laneCustomer").GetString()!, one.GetProperty("from").GetString()!, one.GetProperty("to").GetString()!);
            return Math.Abs(score - one.GetProperty("score").GetDouble()) < 1e-12;
        });
        check(scores, "billing parity: how well a lane fits a job is Booking's own score, Thai names included");
        var bandMax = fixture.RootElement.GetProperty("prices").GetProperty("bandMax").EnumerateArray().Select(one => one.GetDecimal()).ToList();
        var prices = fixture.RootElement.GetProperty("prices").GetProperty("cases").EnumerateArray().All(one =>
        {
            var row = one.GetProperty("row").EnumerateArray()
                .Select((cell, i) => (bandMax[i], cell.ValueKind == JsonValueKind.Null ? (decimal?)null : cell.GetDecimal())).ToList();
            var at = RateMatch.BandIn(row, one.GetProperty("diesel").GetDecimal());
            decimal? price = at < 0 ? null : row[at].Item2;
            decimal? wanted = one.GetProperty("price").ValueKind == JsonValueKind.Null ? null : one.GetProperty("price").GetDecimal();
            return price == wanted;
        });
        check(prices, "billing parity: a lane's price at a diesel figure is the rate book's — its own cheapest covering band, else its top one");
    }
}
