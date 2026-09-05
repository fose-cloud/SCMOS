using System.Text.Json;

namespace Scmos.Api.Rules;

/// <summary>
/// Reading OpenRouteService's answers.
///
/// <para>
/// Separated from the calls that fetch them because the calls cannot be run
/// here: they need a key, and a key is the one thing this codebase's author is
/// not allowed to hold. So the requests were written from the published shapes
/// and every judgement made about the reply — did it find the place, how far is
/// it, is the answer usable — is here, where <c>--check-route</c> runs it
/// against those shapes without a network or an account.
/// </para>
/// <para>
/// The failures worth designing for are not exceptions. They are a reply that
/// parses perfectly and means "no such place", and a reply that means "you are
/// out of quota" — both of which, read carelessly, become zero kilometres and a
/// quotation priced at nothing.
/// </para>
/// </summary>
public static class RouteReading
{
    /// <summary>
    /// The routing profile: a lorry, not a car.
    ///
    /// These are 20- and 40-foot containers on the road between a port and an
    /// industrial estate. A car's route may use a road no lorry is allowed on,
    /// and the distance off it would price a journey that cannot be driven.
    /// </summary>
    public const string Profile = "driving-hgv";

    /// <summary>
    /// Where the register's places are.
    ///
    /// "Amata City" and "LCB Port" are written by people who know which country
    /// they are standing in. Without this, the geocoder is free to answer with
    /// somewhere else and return a confident distance to it.
    /// </summary>
    public const string Country = "TH";

    /// <param name="Found">Whether the geocoder recognised the place at all.</param>
    /// <param name="Lon">Longitude. ORS takes coordinates longitude first, which
    /// is the opposite of how everybody says them out loud.</param>
    /// <param name="Label">What the geocoder thinks it found, shown so a person
    /// can see it looked up the wrong town before trusting the distance.</param>
    public readonly record struct Place(bool Found, double Lon, double Lat, string Label)
    {
        public static readonly Place Missing = new(false, 0, 0, "");
    }

    /// <summary>
    /// The first result from a geocoding reply, or Missing when there is none.
    ///
    /// An empty feature list is the normal, successful shape for "no such
    /// place" — HTTP 200, valid JSON, nothing in it. Read as an error it would
    /// be invisible; read as a coordinate it would be the Gulf of Guinea, which
    /// is where (0, 0) is.
    /// </summary>
    public static Place FirstPlace(string json)
    {
        try
        {
            using var reply = JsonDocument.Parse(json);
            if (!reply.RootElement.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array
                || features.GetArrayLength() == 0) return Place.Missing;

            var first = features[0];
            if (!first.TryGetProperty("geometry", out var geometry)
                || !geometry.TryGetProperty("coordinates", out var pair)
                || pair.ValueKind != JsonValueKind.Array
                || pair.GetArrayLength() < 2) return Place.Missing;

            var label = first.TryGetProperty("properties", out var properties)
                && properties.TryGetProperty("label", out var text)
                && text.ValueKind == JsonValueKind.String
                    ? text.GetString() ?? "" : "";

            return new Place(true, pair[0].GetDouble(), pair[1].GetDouble(), label);
        }
        catch (JsonException)
        {
            return Place.Missing;
        }
    }

    /// <param name="Ok">Whether a distance was actually found.</param>
    /// <param name="Km">Kilometres, rounded to whole. Zero when not Ok.</param>
    public readonly record struct Measured(bool Ok, int Km)
    {
        public static readonly Measured None = new(false, 0);
    }

    /// <summary>
    /// The distance from a directions reply, asked for in kilometres.
    ///
    /// Rounded to whole kilometres because that is what the register stores and
    /// what a rate is quoted against — and because a tenth of a kilometre is
    /// precision this does not have: the geocoder placed both ends at the
    /// nearest thing it recognised, not at the loading bay.
    ///
    /// A route of zero is refused rather than returned. ORS answers 0 when both
    /// ends snap to the same point, which happens when the geocoder failed to
    /// tell two places apart — and a zero distance prices a journey at nothing.
    /// </summary>
    public static Measured Distance(string json)
    {
        try
        {
            using var reply = JsonDocument.Parse(json);
            if (!reply.RootElement.TryGetProperty("routes", out var routes)
                || routes.ValueKind != JsonValueKind.Array
                || routes.GetArrayLength() == 0) return Measured.None;

            if (!routes[0].TryGetProperty("summary", out var summary)
                || !summary.TryGetProperty("distance", out var distance)
                || distance.ValueKind != JsonValueKind.Number) return Measured.None;

            var km = (int)Math.Round(distance.GetDouble(), MidpointRounding.AwayFromZero);
            return km > 0 ? new Measured(true, km) : Measured.None;
        }
        catch (JsonException)
        {
            return Measured.None;
        }
    }

    /// <summary>
    /// What to tell somebody when the service refused, by status code.
    ///
    /// In their own language and about their own situation. "HTTP 403" on a
    /// pricing screen tells the person typing a quotation nothing they can act
    /// on, and the two most likely refusals — a key that is wrong and a quota
    /// that is spent — need completely different people to fix them.
    /// </summary>
    public static string Refusal(int status) => status switch
    {
        401 or 403 => "OpenRouteService ปฏิเสธคีย์ — ตรวจสอบ OpenRouteService__ApiKey "
                      + "ที่ App Service ของ API",
        429 => "ใช้โควตา OpenRouteService ครบแล้วสำหรับช่วงนี้ — พรุ่งนี้ลองใหม่ หรือกรอกระยะทางเอง",
        >= 500 => "OpenRouteService ขัดข้องชั่วคราว — กรอกระยะทางเองไปก่อนได้",
        _ => $"OpenRouteService ตอบกลับไม่สำเร็จ (HTTP {status})",
    };

    /// <summary>
    /// The directions request body.
    ///
    /// Geometry and turn instructions are switched off: this asks one question,
    /// and the road's shape would be tens of kilobytes of answer to a question
    /// about a single number.
    /// </summary>
    public static string DirectionsBody(Place from, Place to) =>
        JsonSerializer.Serialize(new
        {
            coordinates = new[]
            {
                new[] { from.Lon, from.Lat },
                new[] { to.Lon, to.Lat },
            },
            units = "km",
            instructions = false,
            // Asked for now: it is the road the 119 kilometres were counted
            // along, and drawing it is the only way a person can see that the
            // number belongs to a route their lorry could actually take.
            geometry = true,
        });

    /// <summary>
    /// The road itself, as longitude/latitude pairs.
    ///
    /// Two shapes are accepted because only one of them has ever been seen from
    /// here. The documented reply carries <c>geometry</c> as an encoded
    /// polyline — a string — and the GeoJSON variant of the same endpoint
    /// carries it as an array of pairs. Reading both costs a dozen lines and
    /// means the map does not go blank the day the shape is not the one guessed.
    ///
    /// An empty list is a normal answer, never an error: the caller has the two
    /// endpoints from geocoding and draws those, so a route that cannot be read
    /// costs the shape of the road and nothing else.
    /// </summary>
    public static IReadOnlyList<(double Lon, double Lat)> Geometry(string json)
    {
        try
        {
            using var reply = JsonDocument.Parse(json);
            if (!reply.RootElement.TryGetProperty("routes", out var routes)
                || routes.ValueKind != JsonValueKind.Array
                || routes.GetArrayLength() == 0) return [];

            if (!routes[0].TryGetProperty("geometry", out var geometry)) return [];

            if (geometry.ValueKind == JsonValueKind.String)
                return DecodePolyline(geometry.GetString() ?? "");

            if (geometry.ValueKind == JsonValueKind.Object
                && geometry.TryGetProperty("coordinates", out var pairs))
                return ReadPairs(pairs);

            return geometry.ValueKind == JsonValueKind.Array ? ReadPairs(geometry) : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<(double Lon, double Lat)> ReadPairs(JsonElement pairs)
    {
        if (pairs.ValueKind != JsonValueKind.Array) return [];
        var road = new List<(double, double)>(pairs.GetArrayLength());
        foreach (var pair in pairs.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2) continue;
            road.Add((pair[0].GetDouble(), pair[1].GetDouble()));
        }
        return road;
    }

    /// <summary>
    /// Google's encoded polyline, which is what OpenRouteService sends.
    ///
    /// Five decimal places, deltas, zig-zag encoded, six bits to a character.
    /// Written out rather than pulled in because it is thirty lines and the
    /// alternative is a dependency on this codebase's fifth runtime package for
    /// one function.
    ///
    /// A malformed string yields whatever decoded before it went wrong rather
    /// than throwing: half a road drawn is better than a blank panel, and the
    /// distance beside it never came from here.
    /// </summary>
    public static IReadOnlyList<(double Lon, double Lat)> DecodePolyline(string encoded)
    {
        var road = new List<(double, double)>();
        int index = 0, lat = 0, lon = 0;

        while (index < encoded.Length)
        {
            if (!Step(encoded, ref index, out var dLat)) break;
            if (!Step(encoded, ref index, out var dLon)) break;
            lat += dLat;
            lon += dLon;
            road.Add((lon / 1e5, lat / 1e5));
        }
        return road;

        static bool Step(string text, ref int at, out int value)
        {
            int result = 0, shift = 0, b;
            do
            {
                if (at >= text.Length) { value = 0; return false; }
                b = text[at++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);
            // The low bit is the sign, and the rest is the magnitude.
            value = (result & 1) != 0 ? ~(result >> 1) : result >> 1;
            return true;
        }
    }
}
