using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs <see cref="CorrectionRules"/> against the spellings the register
/// actually holds, with <c>--check-corrections</c>. No database: the lists
/// are three names long and the carrier register is a dictionary.
///
/// The cases that matter most are the ones that must come back
/// <b>unchanged</b>: a shorter customer name is not the longer one, a
/// retired type is never proposed, a status nobody can name is not re-filed
/// as new. A rule that proposes something for every cell is the rule that
/// would have an owner approving a guess.
/// </summary>
public static class CorrectionCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-corrections")) return null;

        var failed = 0;
        Console.WriteLine("Proposed corrections: the list's spelling, or nothing.");
        Console.WriteLine();

        var carriers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SJ"] = "Sangja Transport Co., Ltd.", ["SANGJA"] = "Sangja Transport Co., Ltd.", ["SANGJATRANSPORTCOLTD"] = "Sangja Transport Co., Ltd.",
            ["9ISARA"] = "9 Isara Logistics Co., Ltd.", ["9ISARALOGISTICSCOLTD"] = "9 Isara Logistics Co., Ltd.",
        };
        var lists = new CorrectionLists(
            ["1X20'", "1X40'", "1X40' HQ", "1X20' RF", "1X40' RF", "1X6WH", "1X4WH", "COMBINE"],
            ["LOTUS", "LOTUS ASIA", "L'OREAL", "The Chemours", "TERRATEC MACHINERY", "DANA (FREE ZONE)", "DANA (LKB)", "DANA (RAYONG)", "DANA (SAHA AUTO)", "DANA SPICER",
             "HENKEL (BANGPOO)", "HENKEL (BANGPOO) BKK", "HENKEL (HAZCHEM)", "TOA (Bangna)", "U.C.", "TROY (HAZCHEM K.39)", "TROY (Kabin Buri)", "ALTEK INTERNATIONAL", "ALTEK INTERNATIONAL : TANK"],
            spelling => carriers.TryGetValue(new string(spelling.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray()), out var name) ? name : null);

        /* ---- type ---- */
        failed += Say("1X40 REEFER is the reefer forty as the list spells it", To(CorrectionRules.VehicleType("1X40 REEFER", lists.TypeCodes)), "1X40' RF");
        failed += Say("1x20 is the twenty", To(CorrectionRules.VehicleType("1x20", lists.TypeCodes)), "1X20'");
        failed += Say("1X40HC is the tall forty", To(CorrectionRules.VehicleType("1X40HC", lists.TypeCodes)), "1X40' HQ");
        failed += Say("a code on the list as spelled proposes nothing", To(CorrectionRules.VehicleType("1X20'", lists.TypeCodes)), null);
        failed += Say("a code added to the list since (COMBINE) proposes nothing", To(CorrectionRules.VehicleType("COMBINE", lists.TypeCodes)), null);
        failed += Say("a retired code is never proposed", To(CorrectionRules.VehicleType("1X20 DG", lists.TypeCodes)), null);
        failed += Say("a note typed into the type column is left alone", To(CorrectionRules.VehicleType("1X20 DG >> 1X40 DG", lists.TypeCodes)), null);

        /* ---- customer ---- */
        failed += Say("lotus asia is LOTUS ASIA", To(CorrectionRules.Customer("lotus asia", lists.Customers)), "LOTUS ASIA");
        failed += Say("a trailing full stop is not a different customer", To(CorrectionRules.Customer("LOTUS ASIA.", lists.Customers)), "LOTUS ASIA");
        failed += Say("a doubled space is not a different customer", To(CorrectionRules.Customer("Lotus  Asia", lists.Customers)), "LOTUS ASIA");
        failed += Say("a non-breaking space is not a different customer", To(CorrectionRules.Customer("LOTUS" + (char)0xA0 + "ASIA", lists.Customers)), "LOTUS ASIA");
        failed += Say("LOTUS is LOTUS, not LOTUS ASIA", To(CorrectionRules.Customer("LOTUS", lists.Customers)), null);
        failed += Say("LOTUSASIA is the same letters as LOTUS ASIA", To(CorrectionRules.Customer("LOTUSASIA", lists.Customers)), "LOTUS ASIA");
        failed += Say("TOA BANGNA is TOA (Bangna) — punctuation is not a customer", To(CorrectionRules.Customer("TOA BANGNA", lists.Customers)), "TOA (Bangna)");
        failed += Say("U.C is U.C.", To(CorrectionRules.Customer("U.C", lists.Customers)), "U.C.");
        failed += Say("a customer the rotation has never heard of is left alone", To(CorrectionRules.Customer("OPTIDUR", lists.Customers)), null);
        failed += Say("the rotation's own spelling proposes nothing", To(CorrectionRules.Customer("The Chemours", lists.Customers)), null);
        var terratec = CorrectionRules.Customer("TERRATEC", lists.Customers);
        failed += Say("TERRATEC is the one rotation name that begins with it", To(terratec), "TERRATEC MACHINERY");
        failed += Say("and the reason says so", terratec?.Reason.Contains("ชื่อเดียวใน Job Rotation ที่ขึ้นต้นด้วย TERRATEC") == true, true);
        failed += Say("DANA with five sites and no evidence is left for a person", To(CorrectionRules.Customer("DANA", lists.Customers)), null);
        failed += Say("and the report can say it is five sites", CorrectionRules.Sites("DANA", lists.Customers), 5);
        var rayong = CorrectionRules.Customer("DANA", lists.Customers, "XPO-RAYONG");
        failed += Say("DANA delivered to XPO-RAYONG is DANA (RAYONG)", To(rayong), "DANA (RAYONG)");
        failed += Say("by the site rule, and the reason names the evidence", rayong?.Rule == "customer.rotation.site" && rayong.Reason.Contains("ระบุ RAYONG"), true);
        var lkb = CorrectionRules.Customer("DANA", lists.Customers, "XPO  LADKRABANG");
        failed += Say("DANA delivered to XPO LADKRABANG is DANA (LKB) by the department's synonym", To(lkb), "DANA (LKB)");
        failed += Say("and the reason shows the synonym it used", lkb?.Reason.Contains("LADKRABANG = LKB") == true, true);
        failed += Say("LAD KRABANG in two words is the same site", To(CorrectionRules.Customer("DANA", lists.Customers, "WH LAD KRABANG")), "DANA (LKB)");
        failed += Say("a destination naming no site and no synonym is still left alone", To(CorrectionRules.Customer("DANA", lists.Customers, "XPO CHONBURI")), null);
        failed += Say("the report can name the sites a bare name could mean", string.Join(",", CorrectionRules.SiteNames("DANA", lists.Customers)), "DANA (FREE ZONE),DANA (LKB),DANA (RAYONG),DANA (SAHA AUTO),DANA SPICER");
        failed += Say("HENKEL delivered to BANGPOO is HENKEL (BANGPOO), not the BKK one — every site word must be there", To(CorrectionRules.Customer("HENKEL", lists.Customers, "BANGPOO")), "HENKEL (BANGPOO)");
        failed += Say("HENKEL delivered to BANGPOO BKK is the BKK one", To(CorrectionRules.Customer("HENKEL", lists.Customers, "WH BANGPOO / BKK")), "HENKEL (BANGPOO) BKK");
        failed += Say("HENKEL delivered to HAZCHEM is HENKEL (HAZCHEM)", To(CorrectionRules.Customer("HENKEL", lists.Customers, "HAZCHEM")), "HENKEL (HAZCHEM)");
        failed += Say("HENKEL delivered to HAZHEM (a slip) is read through the synonym", To(CorrectionRules.Customer("HENKEL", lists.Customers, "HAZHEM")), "HENKEL (HAZCHEM)");
        var troy = CorrectionRules.Customer("TROY", lists.Customers, "HAZCHEM");
        failed += Say("TROY delivered to HAZCHEM is TROY (HAZCHEM K.39) — the only TROY site that word belongs to", To(troy), "TROY (HAZCHEM K.39)");
        failed += Say("and the reason names the word the destination gave", troy?.Reason.Contains("ระบุ HAZCHEM") == true, true);
        failed += Say("TROY delivered to KABINBURI is TROY (Kabin Buri) through the synonym", To(CorrectionRules.Customer("TROY", lists.Customers, "KABINBURI")), "TROY (Kabin Buri)");
        failed += Say("TROY delivered to KABIBURI (a slip) too", To(CorrectionRules.Customer("TROY", lists.Customers, "KABIBURI")), "TROY (Kabin Buri)");
        failed += Say("DANA delivered to SAHA AUTOPART is DANA (SAHA AUTO) — SAHA belongs to that site alone", To(CorrectionRules.Customer("DANA", lists.Customers, "SAHA AUTOPART")), "DANA (SAHA AUTO)");
        failed += Say("a container number in the destination is not evidence of a site", To(CorrectionRules.Customer("TROY", lists.Customers, "TCNU8067393")), null);
        failed += Say("ALTEK to a warehouse naming neither variant is left alone", To(CorrectionRules.Customer("ALTEK", lists.Customers, "W/H Frasers Property")), null);
        failed += Say("the site is read from the job's own cells through Propose", CorrectionRules.Propose("IMPORT", new Dictionary<string, string> { ["customer"] = "DANA", ["destination"] = "XPO-RAYONG" }, lists).Single().To, "DANA (RAYONG)");

        /* ---- trucker ---- */
        var sj = CorrectionRules.Carrier("SJ", lists.CarrierOf);
        failed += Say("SJ is Sangja Transport as the register names it", To(sj), "Sangja Transport Co., Ltd.");
        failed += Say("and the reason keeps the old spelling for the owner", sj?.Reason.Contains("(เดิมสะกด SJ)") == true, true);
        failed += Say("9ISARA is 9 Isara Logistics", To(CorrectionRules.Carrier("9ISARA", lists.CarrierOf)), "9 Isara Logistics Co., Ltd.");
        failed += Say("the register's own name proposes nothing", To(CorrectionRules.Carrier("Sangja Transport Co., Ltd.", lists.CarrierOf)), null);
        failed += Say("a haulier the register has never seen is left alone", To(CorrectionRules.Carrier("XYZ TRANSPORT", lists.CarrierOf)), null);

        /* ---- category ---- */
        failed += Say("Import is IMPORT", To(CorrectionRules.Category("Import")), "IMPORT");
        failed += Say("a padded EXPORT is EXPORT", To(CorrectionRules.Category("EXPORT ")), "EXPORT");
        failed += Say("IMPORT proposes nothing", To(CorrectionRules.Category("IMPORT")), null);
        failed += Say("a word that is not a category is left alone", To(CorrectionRules.Category("Domestic")), null);

        /* ---- status ---- */
        failed += Say("delivered is DELIVERED", To(CorrectionRules.Status("delivered", "IMPORT")), "DELIVERED");
        failed += Say("Truck Assigned is TRUCK_ASSIGNED", To(CorrectionRules.Status("Truck Assigned", "IMPORT")), "TRUCK_ASSIGNED");
        failed += Say("the old 'waiting truck' is WAITING_SUPPLIER", To(CorrectionRules.Status("waiting truck", "EXPORT")), "WAITING_SUPPLIER");
        failed += Say("a code on the ladder proposes nothing", To(CorrectionRules.Status("DELIVERED", "IMPORT")), null);
        failed += Say("a stage nobody can name is not re-filed as DRAFT", To(CorrectionRules.Status("xyz", "IMPORT")), null);
        failed += Say("COMPLETED stays COMPLETED", To(CorrectionRules.Status("COMPLETED", "EXPORT")), null);

        /* ---- the delay reason ---- */
        var today = new DateOnly(2026, 9, 22);
        JobRecord Late(string cat, string date, string plan, string arrDate, string arrTime, string customer = "OPTIDUR", string reason = "", string destination = "", string plant = "", string status = "DELIVERED")
            => new() { Key = "J", Cat = cat, Date = date, PlanTime = plan, ArrDate = arrDate, ArrTime = arrTime, Customer = customer, Reason = reason, Destination = destination, Plant = plant, Status = status, JobCode = "260900760079" };
        failed += Say("every catalogue sentence files under its own category, so the KPI counts it",
            string.Join(",", DelayReasonRule.Catalogue.Select(one => DelayReasons.Classify(one.Text).Category == one.Category ? "ok" : one.Text)), string.Join(",", DelayReasonRule.Catalogue.Select(_ => "ok")));
        var breakdown = DelayReasonRule.Propose(Late("IMPORT", "21/09/2026", "09:00", "21/09/2026", "10:15", destination: "WH ALLNEX"), ["รถเสียกลางทาง รอช่างครับ", "ออกจากท่าแล้ว"], today);
        failed += Say("a late import whose haulier said the truck broke down is proposed Truck Breakdown", breakdown?.To, "Truck Breakdown");
        failed += Say("by the carrier rule, quoting the message and the lateness", breakdown?.Rule == "reason.carrier" && breakdown.Reason.Contains("75 นาที") && breakdown.Reason.Contains("รถเสียกลางทาง"), true);
        var port = DelayReasonRule.Propose(Late("EXPORT", "21/09/2026", "09:00", "21/09/2026", "11:00", plant: "LOTUS ASIA", destination: "", status: "COMPLETED"), [], today);
        failed += Say("a late export with no message and no port on the leg is proposed the department's main reason", port?.To, "Delay due to Traffic Congestion");
        var lcb = DelayReasonRule.Propose(Late("IMPORT", "21/09/2026", "09:00", "21/09/2026", "11:00", destination: "LCB TERMINAL B"), ["สวัสดีครับ"], today);
        failed += Say("a late import off a port is proposed Port Traffic Congestion", lcb?.To, "Port Traffic Congestion");
        failed += Say("by the route rule, naming the port and that another may be picked", lcb?.Rule == "reason.route" && lcb.Reason.Contains("LCB TERMINAL B") && lcb.Reason.Contains("เลือกเหตุผลอื่นได้"), true);
        failed += Say("a shipment on time is not asked", DelayReasonRule.Propose(Late("IMPORT", "21/09/2026", "09:00", "21/09/2026", "09:00"), [], today), null);
        failed += Say("Lotus twenty minutes late is on time by its own term — not asked", DelayReasonRule.Propose(Late("EXPORT", "21/09/2026", "09:00", "21/09/2026", "09:20", customer: "LOTUS"), [], today), null);
        failed += Say("Lotus forty minutes late is asked", DelayReasonRule.Propose(Late("EXPORT", "21/09/2026", "09:00", "21/09/2026", "09:40", customer: "LOTUS"), [], today)?.To, "Delay due to Traffic Congestion");
        failed += Say("a reason already typed is never proposed over", DelayReasonRule.Propose(Late("IMPORT", "21/09/2026", "09:00", "21/09/2026", "11:00", reason: "รถติดหน้าท่า"), [], today), null);
        failed += Say("a cancelled job is not asked", DelayReasonRule.Propose(Late("IMPORT", "21/09/2026", "09:00", "21/09/2026", "11:00", status: "CANCELLED"), [], today), null);
        failed += Say("a delivery job is not asked — IMPORT and EXPORT only", DelayReasonRule.Propose(Late("DELIVERY", "21/09/2026", "09:00", "21/09/2026", "11:00"), [], today), null);
        failed += Say("a shipment older than the look-back is not asked", DelayReasonRule.Propose(Late("IMPORT", "01/06/2026", "09:00", "01/06/2026", "11:00"), [], today), null);
        failed += Say("a shipment not yet arrived is not asked", DelayReasonRule.Propose(Late("IMPORT", "21/09/2026", "09:00", "", ""), [], today), null);
        failed += Say("the catalogue knows its own texts and nothing else", DelayReasonRule.IsCatalogued("Port Traffic Congestion") && !DelayReasonRule.IsCatalogued("รถติด"), true);

        /* ---- a job's cells together ---- */
        var cells = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cat"] = "EXPORT", ["customer"] = "lotus asia", ["trucker"] = "SJ", ["type"] = "1X40 REEFER", ["status"] = "",
        };
        var proposals = CorrectionRules.Propose("EXPORT", cells, lists);
        failed += Say("a job's proposals are one per off-list cell, in column order", string.Join(",", proposals.Select(p => p.Field)), "customer,trucker,type");
        failed += Say("a blank cell proposes nothing", proposals.Any(p => p.Field == "status"), false);
        failed += Say("every proposal names its rule", proposals.All(p => p.Rule.Length > 0 && p.Reason.Length > 0 && p.From != p.To), true);
        failed += Say("the labels name every dropdown column", CorrectionRules.Fields.All(CorrectionRules.Labels.ContainsKey), true);

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "Every proposal is a list's spelling; everything else is left for a person." : $"{failed} check(s) failed.");
        return failed == 0 ? 0 : 1;
    }

    private static string? To(Correction? one) => one?.To;

    private static int Say<T>(string why, T got, T want)
    {
        var ok = EqualityComparer<T>.Default.Equals(got, want);
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {why,-70} {(ok ? "" : $"got {got ?? (object)"null"}  want {want ?? (object)"null"}")}");
        return ok ? 0 : 1;
    }
}
