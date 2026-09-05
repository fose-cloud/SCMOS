using System.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Ok">Whether there is a distance to offer.</param>
/// <param name="Km">Kilometres by road, whole. Zero when not Ok.</param>
/// <param name="Message">Why there is no distance, in the operator's language.</param>
/// <param name="FromLabel">What the geocoder decided the origin was.</param>
/// <param name="ToLabel">Likewise the destination. Both shown, because the
/// commonest wrong answer is a confident distance to the wrong town.</param>
/// <param name="Path">
/// The road, as flat [lon, lat, lon, lat, …].
///
/// Flat rather than nested pairs because it halves the JSON: a hundred-mile
/// route is hundreds of points and every one of them would otherwise carry two
/// brackets. Empty is normal — the screen draws the two ends instead.
/// </param>
public record RouteEstimate(bool Ok, int Km, string Message, string FromLabel, string ToLabel,
    IReadOnlyList<double> Path)
{
    public static RouteEstimate No(string why) => new(false, 0, why, "", "", []);
}

/// <summary>
/// How far apart two places are by road, from OpenRouteService.
///
/// <para>
/// A suggestion, never a decision. The quotation screen shows what this returns
/// and a person presses a button to accept it; the stored distance still
/// carries the name of whoever agreed, because a distance is a judgement and a
/// routing engine has not seen the gate the lorry actually uses.
/// </para>
/// <para>
/// Chosen over Google's Distance Matrix because it is free at the volume this
/// team works at — a few dozen quotations a day against a quota of thousands —
/// and because it needs no billing account behind it.
/// </para>
/// <para>
/// The key is read from configuration and never appears in this repository.
/// With no key configured, every call returns a refusal that says which setting
/// is missing, and the screen carries on exactly as it did before this existed.
/// </para>
/// </summary>
public class RoutingService(
    IHttpClientFactory factory, IConfiguration config, IMemoryCache cache,
    ILogger<RoutingService> log)
{
    public const string ClientName = "openrouteservice";

    /// <summary>
    /// Where the service lives.
    ///
    /// api.openrouteservice.org was deprecated on 28 April 2026 in favour of
    /// api.heigit.org, which is where the two paths below now hang: the router
    /// under /openrouteservice and the geocoder under /pelias. The old host
    /// still answers on a reduced quota, which is the worst kind of deprecation
    /// — it works until it quietly does not.
    ///
    /// Settable, so a host that moves again does not need a deploy.
    /// </summary>
    private const string HostSetting = "OpenRouteService:BaseUrl";
    private const string DefaultHost = "https://api.heigit.org";

    /// <summary>The paths, exposed so `--check-route` can pin them.</summary>
    public const string DefaultHostFor = DefaultHost;
    public const string GeocodePath = "/pelias/v1/search";
    public static string DirectionsPath => $"/openrouteservice/v2/directions/{RouteReading.Profile}";

    /// <summary>Where the key lives. An App Service setting, or a Key Vault reference.</summary>
    private const string KeySetting = "OpenRouteService:ApiKey";

    private string Host => (config[HostSetting] ?? "").Trim().TrimEnd('/') is { Length: > 0 } set
        ? set : DefaultHost;

    /// <summary>
    /// How long a measured route is held.
    ///
    /// A road between a port and an industrial estate does not move. This is
    /// only here so that somebody adjusting a quotation and pressing the button
    /// four times spends one unit of a shared daily quota rather than twelve.
    /// </summary>
    private static readonly TimeSpan Remember = TimeSpan.FromHours(12);

    /// <summary>
    /// How many points of the road are worth sending.
    ///
    /// The panel it is drawn in is a few hundred pixels across, so past this
    /// every extra point lands on a pixel already covered — it costs payload
    /// and draws nothing.
    /// </summary>
    private const int MaxPathPoints = 200;

    private string Key => (config[KeySetting] ?? "").Trim();

    /// <summary>Whether the screen should offer the button at all.</summary>
    public bool Configured => Key.Length > 0;

    /// <summary>
    /// Puts the key on a request, without asking whether it is a legal scheme.
    ///
    /// <c>new AuthenticationHeaderValue(key)</c> reads its one argument as the
    /// authentication <b>scheme</b> — "Bearer", "Basic" — and a scheme has to be
    /// an HTTP token, which excludes "=" and "/". OpenRouteService now issues
    /// JWT keys, which are base64 and contain both, so that constructor threw a
    /// FormatException on the real key and the endpoint answered 500. Nothing in
    /// the fixture caught it, because the fixtures never held a key.
    ///
    /// The header goes on unvalidated: it is not a scheme and a parameter, it is
    /// the whole value, exactly as OpenRouteService expects to read it.
    /// </summary>
    public static void Authorise(System.Net.Http.Headers.HttpRequestHeaders headers, string key) =>
        headers.TryAddWithoutValidation("Authorization", key);

    public async Task<RouteEstimate> MeasureAsync(string from, string to, CancellationToken token)
    {
        var origin = Formats.Clean(from);
        var destination = Formats.Clean(to);
        if (origin.Length == 0 || destination.Length == 0)
            return RouteEstimate.No("ต้องมีทั้งต้นทางและปลายทางก่อนจึงจะวัดระยะทางได้");

        if (!Configured)
            // Named the way Azure takes it, not the way .NET reads it. The
            // person seeing this is standing in the portal with the box open,
            // and "OpenRouteService:ApiKey" is a key they cannot type there —
            // App Service maps a double underscore onto the colon.
            return RouteEstimate.No(
                "ยังไม่ได้ตั้งค่า OpenRouteService — เพิ่ม OpenRouteService__ApiKey "
                + "(ขีดล่างสองอัน) ที่ App Service ของ API แล้วรีสตาร์ท");

        var cacheKey = $"route::{JourneyKey.Of(origin, destination)}";
        if (cache.TryGetValue(cacheKey, out RouteEstimate? held) && held is not null) return held;

        try
        {
            // Which half failed, said separately. "No distance" covers a place
            // the map does not know and a geocoder that refused the key, and
            // those need completely different things done about them.
            var start = await GeocodeAsync(origin, token);
            if (start.Refused != 0) return RouteEstimate.No("ค้นหาสถานที่: " + RouteReading.Refusal(start.Refused));
            if (!start.Place.Found) return RouteEstimate.No($"หาต้นทางไม่พบบนแผนที่: {origin}");

            var end = await GeocodeAsync(destination, token);
            if (end.Refused != 0) return RouteEstimate.No("ค้นหาสถานที่: " + RouteReading.Refusal(end.Refused));
            if (!end.Place.Found) return RouteEstimate.No($"หาปลายทางไม่พบบนแผนที่: {destination}");

            var client = factory.CreateClient(ClientName);
            Authorise(client.DefaultRequestHeaders, Key);

            var body = new StringContent(
                RouteReading.DirectionsBody(start.Place, end.Place), Encoding.UTF8, "application/json");
            var reply = await client.PostAsync(
                $"{Host}{DirectionsPath}", body, token);

            if (!reply.IsSuccessStatusCode)
                return RouteEstimate.No(RouteReading.Refusal((int)reply.StatusCode));

            var reply2 = await reply.Content.ReadAsStringAsync(token);
            var measured = RouteReading.Distance(reply2);
            if (!measured.Ok)
                return RouteEstimate.No("OpenRouteService หาเส้นทางรถบรรทุกระหว่างสองจุดนี้ไม่ได้");

            /*
             * The road, thinned.
             *
             * A hundred-kilometre route comes back as hundreds of points, and
             * the map it is drawn on is a few hundred pixels wide — past a
             * couple of hundred points every extra one lands on a pixel already
             * covered. Both ends are always kept, because a route that stops
             * short of the place it was measured to would be the map saying
             * something the distance does not.
             */
            var road = RouteReading.Geometry(reply2);
            var path = new List<double>();
            var step = Math.Max(1, road.Count / MaxPathPoints);
            for (var at = 0; at < road.Count; at += step)
            {
                path.Add(road[at].Lon);
                path.Add(road[at].Lat);
            }
            if (road.Count > 0 && step > 1)
            {
                path.Add(road[^1].Lon);
                path.Add(road[^1].Lat);
            }

            var answer = new RouteEstimate(true, measured.Km, "",
                start.Place.Label, end.Place.Label, path);
            cache.Set(cacheKey, answer, Remember);
            return answer;
        }
        catch (TaskCanceledException)
        {
            // A timeout, or the caller navigating away. Neither is an error the
            // person needs explained as one — they can type the number.
            return RouteEstimate.No("OpenRouteService ตอบช้าเกินไป — กรอกระยะทางเองไปก่อนได้");
        }
        catch (HttpRequestException problem)
        {
            // Logged without the key, which is never part of a message: the URL
            // carries it as a query parameter on the geocoding call.
            log.LogWarning(problem, "Could not reach OpenRouteService.");
            return RouteEstimate.No("ติดต่อ OpenRouteService ไม่ได้ — กรอกระยะทางเองไปก่อนได้");
        }
        catch (Exception problem)
        {
            /*
             * Nothing from a suggested distance may reach the browser as a 500.
             *
             * One did: a FormatException from building the Authorization header,
             * thrown before any request went out, which the screen could only
             * report as "could not read the distance" — the least useful thing
             * it could have said about the most findable bug in this file.
             *
             * A quotation screen has a working answer without this service at
             * all, so every failure here is a sentence and a person typing the
             * number, never a stack trace.
             */
            log.LogError(problem, "OpenRouteService lookup failed unexpectedly.");
            return RouteEstimate.No(
                "วัดระยะทางไม่สำเร็จ (ระบบภายใน) — กรอกระยะทางเองไปก่อนได้ · " + problem.GetType().Name);
        }
    }

    /// <summary>
    /// One place name to a coordinate, biased to Thailand.
    ///
    /// One result asked for, because the screen offers a distance rather than a
    /// choice of places — and the label travels back so a person can see it
    /// looked up the wrong town before trusting the number.
    /// </summary>
    private async Task<(RouteReading.Place Place, int Refused)> GeocodeAsync(
        string place, CancellationToken token)
    {
        // Built rather than concatenated: the register holds places like
        // "W/H OPTIDUR" and "Frasers Property, Bangpakong", and an unescaped
        // slash or ampersand truncates the search into a different question
        // that still returns a confident answer.
        var query = new Dictionary<string, string?>
        {
            ["api_key"] = Key,
            ["text"] = place,
            ["boundary.country"] = RouteReading.Country,
            ["size"] = "1",
        };
        var url = QueryHelpers.AddQueryString($"{Host}{GeocodePath}", query);

        var client = factory.CreateClient(ClientName);
        // Both ways at once. The query parameter is how the geocoder has always
        // taken a key; the header is how the router takes one. Sending both
        // means the first real call works whichever the moved host reads, which
        // matters more than tidiness for a request that has never been made.
        Authorise(client.DefaultRequestHeaders, Key);

        var reply = await client.GetAsync(url, token);
        if (!reply.IsSuccessStatusCode) return (RouteReading.Place.Missing, (int)reply.StatusCode);
        return (RouteReading.FirstPlace(await reply.Content.ReadAsStringAsync(token)), 0);
    }
}
