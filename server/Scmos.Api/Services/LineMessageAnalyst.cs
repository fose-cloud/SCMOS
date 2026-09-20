using System.ClientModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// The model laying out a message about several boxes, one line per box,
/// for the parser to read.
///
/// <para>
/// Asked for on 17 Sep 2026 — "ให้ AI ช่วยวิเคราะห์ก่อนนำไปใส่ในตารางงาน". The rule
/// in <see cref="LineBlocks"/> cuts a message that is already laid out in
/// lines; what it cannot cut — two boxes named on one line, a status that
/// applies to both, a truck written once for two trips — is handed here.
/// The model is asked to do one thing: rewrite the message as one line per
/// box or job number, each line carrying that box's own words — its status,
/// its plate, its driver, its number, its clock — copied, never composed.
/// Every line then goes to the deterministic parser, which decides what the
/// words mean, and to the same approval every message gets. The model
/// arranges; it does not read, and it does not write.
/// </para>
///
/// <para>Off unless the model key is set; off by <c>Line__AnalyseWithAi=false</c>.</para>
/// </summary>
public interface ILineMessageAnalyst
{
    bool Configured { get; }

    /// <summary>The message as one line per box or job, or empty when the model could not lay it out.</summary>
    Task<IReadOnlyList<string>> LayOutAsync(string text, CancellationToken token);

    /// <summary>
    /// The truck's details out of a message the rules could not read — the
    /// driver, the number, the plate, each as written — or null when the
    /// model says the message is not giving a truck's details.
    /// </summary>
    Task<LineMessageAnalyst.TruckDetails?> ReadDetailsAsync(string text, CancellationToken token);
}

public sealed class LineMessageAnalyst(IOptions<OpenAiOptions> openAi, IConfiguration config, ILogger<LineMessageAnalyst> log)
    : ILineMessageAnalyst
{
    public const string SwitchKey = "Line:AnalyseWithAi";

    public const string Instructions =
        "You are given one LINE message from a Thai haulier's driver or dispatcher about container trucking jobs. " +
        "It may mention several containers (owner code of four letters and seven digits, such as TXGU6125873) or " +
        "several twelve-digit job numbers. Rewrite it as one line per container or job number. Each line must start " +
        "with that container or job number and then carry, copied word for word from the message, everything that " +
        "belongs to it: the status words (such as บรรจุเสร็จแล้ว, ถึงโรงงาน 10:30, ออกจากท่าแล้ว), the truck plate " +
        "(such as 75-1724), the driver's name, the phone number, and any time. Text that applies to all of them — a " +
        "booking reference, a customer name, a shared status — is repeated on every line. Never invent, translate " +
        "or normalise anything; never add a container that is not in the message. If the message is about one " +
        "container or job only, return a single line.";

    public const string Schema =
        "{\"type\":\"object\",\"properties\":{" +
        "\"lines\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}," +
        "\"required\":[\"lines\"],\"additionalProperties\":false}";

    /// <summary>What the model read: each as written in the message, or null.</summary>
    public record TruckDetails(string? Driver, string? Phone, string? Plate);

    /// <summary>
    /// Asked for on 18 Sep 2026, when a haulier answered the 09:00 reminder
    /// with "พขร.เต๋า ใจเงิน / 085-089-2487 / ทะเบียน71-5111ชบ. ค่ะ" — a name, a
    /// number and a plate, naming no job. The rules read that shape now;
    /// what they still cannot — spaces for dashes, a plate run into a
    /// word — the model reads, and its reading is held to the rules' own
    /// shapes in <see cref="ReadDetails"/> before anything believes it.
    /// </summary>
    public const string DetailsInstructions =
        "You are given one LINE message from a Thai haulier's room about container trucking. Decide whether the " +
        "message is the haulier giving a truck's details for a job — the driver's name, the driver's phone number, " +
        "the truck's licence plate — typically in answer to a request for them. If it is, copy each detail exactly " +
        "as written in the message, never translated, normalised or invented: the driver's name without titles or " +
        "labels such as พขร., คนขับ, ชื่อ, นาย, นาง, คุณ; the phone number; the licence plate (digits, a dash and " +
        "digits, or Thai letters and digits) with its province if one is written. If the message is not giving a " +
        "truck's details, set isTruckDetails to false. A detail the message does not carry is null.";

    public const string DetailsSchema =
        "{\"type\":\"object\",\"properties\":{" +
        "\"isTruckDetails\":{\"type\":\"boolean\"}," +
        "\"driver\":{\"type\":[\"string\",\"null\"]}," +
        "\"phone\":{\"type\":[\"string\",\"null\"]}," +
        "\"plate\":{\"type\":[\"string\",\"null\"]}}," +
        "\"required\":[\"isTruckDetails\",\"driver\",\"phone\",\"plate\"],\"additionalProperties\":false}";

    public bool Configured => openAi.Value.ApiKey.Length > 0 && config.GetValue(SwitchKey, true);

    public async Task<TruckDetails?> ReadDetailsAsync(string text, CancellationToken token)
    {
        if (!Configured || string.IsNullOrWhiteSpace(text)) return null;

        var settings = openAi.Value;
        var credential = new ApiKeyCredential(settings.ApiKey);
        var client = settings.Endpoint.Length == 0
            ? new ChatClient(settings.Model, credential)
            : new ChatClient(settings.Model, credential, new OpenAIClientOptions { Endpoint = new Uri(settings.Endpoint) });
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "truck_details",
                jsonSchema: BinaryData.FromString(DetailsSchema),
                jsonSchemaIsStrict: true),
        };

        var completion = await client.CompleteChatAsync(
            [new SystemChatMessage(DetailsInstructions), new UserChatMessage(text)], options, token);
        var answer = completion.Value.Content.FirstOrDefault(block => block.Text is { Length: > 0 })?.Text;
        var found = ReadDetails(answer);
        log.LogInformation("LINE analyst read a message as {What}", found is null ? "not a truck's details" : "a truck's details");
        return found;
    }

    /// <summary>
    /// The model's reading, held to the rules' own shapes: the plate must
    /// read as a plate, the number as a number, the name as Thai words
    /// with no label in them. Null when the model said no, or when nothing
    /// survives. Pure, for the check.
    /// </summary>
    public static TruckDetails? ReadDetails(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;
        string? driver, phone, plate;
        try
        {
            using var json = JsonDocument.Parse(answer);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("isTruckDetails", out var yes) || yes.ValueKind != JsonValueKind.True) return null;
            driver = Text(root, "driver");
            phone = Text(root, "phone");
            plate = Text(root, "plate");
        }
        catch (JsonException) { return null; }

        var plates = plate is null ? [] : LineParser.FindPlates(LineParser.Normalise(plate));
        var readPlate = plates.Count == 1 ? plates[0] : null;
        var readPhone = phone is null ? null : LineParser.FindPhone(LineParser.Normalise(phone));
        var readDriver = driver is null ? null : LineParser.FindDriver(LineParser.Normalise(driver), plates, readPhone ?? "");
        if (readPlate is null && readPhone is null && readDriver is null) return null;
        return new TruckDetails(readDriver, readPhone, readPlate);
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(found.GetString()) ? found.GetString() : null;

    public async Task<IReadOnlyList<string>> LayOutAsync(string text, CancellationToken token)
    {
        if (!Configured || string.IsNullOrWhiteSpace(text)) return [];

        var settings = openAi.Value;
        var credential = new ApiKeyCredential(settings.ApiKey);
        var client = settings.Endpoint.Length == 0
            ? new ChatClient(settings.Model, credential)
            : new ChatClient(settings.Model, credential, new OpenAIClientOptions { Endpoint = new Uri(settings.Endpoint) });
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "message_lines",
                jsonSchema: BinaryData.FromString(Schema),
                jsonSchemaIsStrict: true),
        };

        var completion = await client.CompleteChatAsync(
            [new SystemChatMessage(Instructions), new UserChatMessage(text)], options, token);
        var answer = completion.Value.Content.FirstOrDefault(block => block.Text is { Length: > 0 })?.Text;
        var lines = Read(answer);
        log.LogInformation("LINE analyst laid a message out as {Count} line(s)", lines.Count);
        return lines;
    }

    /// <summary>
    /// The model's lines, kept only when each names exactly one box or job
    /// and none names one the message did not — the model arranges, it
    /// does not add. Pure, for the check.
    /// </summary>
    public static IReadOnlyList<string> Read(string? answer, string? original = null)
    {
        if (string.IsNullOrWhiteSpace(answer)) return [];
        List<string> lines;
        try
        {
            using var json = JsonDocument.Parse(answer);
            if (!json.RootElement.TryGetProperty("lines", out var list) || list.ValueKind != JsonValueKind.Array) return [];
            lines = [.. list.EnumerateArray().Where(one => one.ValueKind == JsonValueKind.String).Select(one => (one.GetString() ?? "").Trim()).Where(one => one.Length > 0)];
        }
        catch (JsonException) { return []; }

        var known = original is null ? null : new HashSet<string>(Keys(original), StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var keys = Keys(line).ToList();
            if (keys.Count != 1) return [];
            if (known is not null && !known.Contains(keys[0])) return [];
        }
        return lines;
    }

    /// <summary>The boxes and job numbers a line names, as the parser will read them.</summary>
    private static IEnumerable<string> Keys(string text)
    {
        var normalised = LineParser.Normalise(text);
        foreach (var number in JobCodes.All(normalised)) yield return number;
        foreach (var box in LineParser.FindContainers(normalised)) yield return box;
    }
}
