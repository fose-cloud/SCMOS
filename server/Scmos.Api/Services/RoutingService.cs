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
public record RouteEstimate(bool Ok, int Km, string Message, string FromLabel, string ToLabel)
{
    public static RouteEstimate No(string why) => new(false, 0, why, "", "");
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
    private const string Host = "https://api.openrouteservice.org";

    /// <summary>Where the key lives. An App Service setting, or a Key Vault reference.</summary>
    private const string KeySetting = "OpenRouteService:ApiKey";

    /// <summary>
    /// How long a measured route is held.
    ///
    /// A road between a port and an industrial estate does not move. This is
    /// only here so that somebody adjusting a quotation and pressing the button
    /// four times spends one unit of a shared daily quota rather than twelve.
    /// </summary>
    private static readonly TimeSpan Remember = TimeSpan.FromHours(12);

    private string Key => (config[KeySetting] ?? "").Trim();

    /// <summary>Whether the screen should offer the button at all.</summary>
    public bool Configured => Key.Length > 0;

    public async Task<RouteEstimate> MeasureAsync(string from, string to, CancellationToken token)
    {
        var origin = Formats.Clean(from);
        var destination = Formats.Clean(to);
        if (origin.Length == 0 || destination.Length == 0)
            return RouteEstimate.No("ต้องมีทั้งต้นทางและปลายทางก่อนจึงจะวัดระยะทางได้");

        if (!Configured)
            return RouteEstimate.No(
                "ยังไม่ได้ตั้งค่า OpenRouteService — ตั้ง OpenRouteService:ApiKey ที่ App Service แล้วรีสตาร์ท");

        var cacheKey = $"route::{JourneyKey.Of(origin, destination)}";
        if (cache.TryGetValue(cacheKey, out RouteEstimate? held) && held is not null) return held;

        try
        {
            var start = await GeocodeAsync(origin, token);
            if (!start.Found) return RouteEstimate.No($"หาต้นทางไม่พบบนแผนที่: {origin}");

            var end = await GeocodeAsync(destination, token);
            if (!end.Found) return RouteEstimate.No($"หาปลายทางไม่พบบนแผนที่: {destination}");

            var client = factory.CreateClient(ClientName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(Key);

            var body = new StringContent(
                RouteReading.DirectionsBody(start, end), Encoding.UTF8, "application/json");
            var reply = await client.PostAsync(
                $"{Host}/v2/directions/{RouteReading.Profile}", body, token);

            if (!reply.IsSuccessStatusCode)
                return RouteEstimate.No(RouteReading.Refusal((int)reply.StatusCode));

            var measured = RouteReading.Distance(await reply.Content.ReadAsStringAsync(token));
            if (!measured.Ok)
                return RouteEstimate.No("OpenRouteService หาเส้นทางรถบรรทุกระหว่างสองจุดนี้ไม่ได้");

            var answer = new RouteEstimate(true, measured.Km, "", start.Label, end.Label);
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
    }

    /// <summary>
    /// One place name to a coordinate, biased to Thailand.
    ///
    /// One result asked for, because the screen offers a distance rather than a
    /// choice of places — and the label travels back so a person can see it
    /// looked up the wrong town before trusting the number.
    /// </summary>
    private async Task<RouteReading.Place> GeocodeAsync(string place, CancellationToken token)
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
        var url = QueryHelpers.AddQueryString($"{Host}/geocode/search", query);
        var client = factory.CreateClient(ClientName);
        var reply = await client.GetAsync(url, token);
        if (!reply.IsSuccessStatusCode) return RouteReading.Place.Missing;
        return RouteReading.FirstPlace(await reply.Content.ReadAsStringAsync(token));
    }
}
