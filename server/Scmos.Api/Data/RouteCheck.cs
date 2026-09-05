using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the OpenRouteService readings against recorded reply shapes, with
/// <c>--check-route</c>.
///
/// <para>
/// This check exists because the live call cannot be made here. It needs an
/// account key, and handling one is not something this codebase's author may
/// do — so the requests were written from the published shapes and never once
/// run against the real service before being deployed. Everything that can be
/// checked without a key is checked here, and what remains untested is exactly
/// one thing: whether OpenRouteService still answers in these shapes.
/// </para>
/// <para>
/// The cases that matter are the ones that parse. A reply meaning "no such
/// place" is HTTP 200 with valid JSON and an empty list; read carelessly it
/// becomes the coordinate (0, 0), which is in the Gulf of Guinea, and a
/// confident distance to it.
/// </para>
/// </summary>
public static class RouteCheck
{
    private const string FoundPlace = """
    {"type":"FeatureCollection","features":[
      {"type":"Feature",
       "geometry":{"type":"Point","coordinates":[100.8833,13.0827]},
       "properties":{"label":"Laem Chabang Port, Chonburi, Thailand","country_a":"THA"}}
    ]}
    """;

    private const string NoSuchPlace = """
    {"type":"FeatureCollection","features":[]}
    """;

    private const string Routed = """
    {"routes":[{"summary":{"distance":118.4,"duration":5820.3}}],
     "metadata":{"engine":{"version":"9.0.0"}}}
    """;

    private const string RoutedZero = """
    {"routes":[{"summary":{"distance":0,"duration":0}}]}
    """;

    private const string NoRoute = """
    {"routes":[]}
    """;

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-route")) return null;

        var failed = 0;
        Console.WriteLine($"Profile {RouteReading.Profile}, places biased to {RouteReading.Country}.");
        Console.WriteLine();

        /* ---- reading a place ---- */
        var found = RouteReading.FirstPlace(FoundPlace);
        failed += Say("a place the geocoder knows comes back found", found.Found, true);
        failed += Say("longitude first, the way ORS takes coordinates", found.Lon, 100.8833);
        failed += Say("and latitude second", found.Lat, 13.0827);
        failed += Say("with the label, so a person can see it found the right town",
            found.Label, "Laem Chabang Port, Chonburi, Thailand");

        Console.WriteLine();
        // The one that would be invisible: HTTP 200, valid JSON, nothing in it.
        var missing = RouteReading.FirstPlace(NoSuchPlace);
        failed += Say("an empty answer is not found", missing.Found, false);
        failed += Say("and is never read as a coordinate", missing.Lon, 0.0);
        failed += Say("nor is a reply that is not JSON at all",
            RouteReading.FirstPlace("<html>502 Bad Gateway</html>").Found, false);
        failed += Say("nor one shaped like something else entirely",
            RouteReading.FirstPlace("""{"error":"quota"}""").Found, false);
        failed += Say("nor a feature with no geometry",
            RouteReading.FirstPlace("""{"features":[{"properties":{"label":"x"}}]}""").Found, false);

        /* ---- reading a distance ---- */
        Console.WriteLine();
        var measured = RouteReading.Distance(Routed);
        failed += Say("a routed journey has a distance", measured.Ok, true);
        // 118.4 to 118: whole kilometres are what the register stores, and a
        // tenth is precision this does not have — the geocoder placed both ends
        // at the nearest thing it recognised, not at the loading bay.
        failed += Say("rounded to the whole kilometre the register stores", measured.Km, 118);

        failed += Say("a zero-kilometre route is refused, not returned",
            RouteReading.Distance(RoutedZero).Ok, false);
        failed += Say("and reports no distance rather than a distance of nothing",
            RouteReading.Distance(RoutedZero).Km, 0);
        failed += Say("no route means no distance", RouteReading.Distance(NoRoute).Ok, false);
        failed += Say("nor does an error body become one",
            RouteReading.Distance("""{"error":{"code":2010}}""").Ok, false);
        failed += Say("nor does junk", RouteReading.Distance("not json").Ok, false);

        // 0.5 rounds away from zero, not to even. Banker's rounding would turn
        // a 0.5 km spur into 0 and refuse the whole route.
        failed += Say("a half kilometre rounds up",
            RouteReading.Distance("""{"routes":[{"summary":{"distance":0.5}}]}""").Km, 1);

        /* ---- the request that gets sent ---- */
        Console.WriteLine();
        var body = RouteReading.DirectionsBody(
            new RouteReading.Place(true, 100.8833, 13.0827, "LCB"),
            new RouteReading.Place(true, 101.0, 13.5, "Amata"));
        failed += Say("coordinates go out longitude first",
            body.Contains("[100.8833,13.0827]"), true);
        failed += Say("kilometres are asked for, so nothing converts metres by hand",
            body.Contains("\"units\":\"km\""), true);
        failed += Say("the road's shape is not requested — it is not the question",
            body.Contains("\"geometry\":false"), true);

        /* ---- what a refusal is called ---- */
        Console.WriteLine();
        // Two refusals, two entirely different people to go and see.
        failed += Say("a rejected key names the setting to check",
            RouteReading.Refusal(403).Contains("ApiKey"), true);
        failed += Say("a spent quota says to type the distance instead",
            RouteReading.Refusal(429).Contains("กรอกระยะทางเอง"), true);
        failed += Say("an outage says the same, without blaming the key",
            RouteReading.Refusal(503).Contains("ApiKey"), false);

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "Every reading holds. What is still untested is whether ORS answers in these shapes."
            : $"{failed} problem(s).");
        return failed == 0 ? 0 : 1;
    }

    private static int Say<T>(string why, T got, T want)
    {
        var ok = EqualityComparer<T>.Default.Equals(got, want);
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {why,-62} {(ok ? "" : $"got {got}  want {want}")}");
        return ok ? 0 : 1;
    }
}
