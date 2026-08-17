using System.ClientModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Scmos.Api.Services;

public class OpenAiOptions
{
    public const string Section = "OpenAI";

    public string ApiKey { get; set; } = "";

    /// <summary>Left empty for api.openai.com; set to point at a compatible endpoint.</summary>
    public string Endpoint { get; set; } = "";

    public string Model { get; set; } = "gpt-4.1";
}

public record ExtractionResult(Dictionary<string, string>? Fields, string? Error, int Status);

public interface IDocumentExtractor
{
    bool Configured { get; }
    Task<ExtractionResult> ReadAsync(string category, IReadOnlyList<DocumentPart> parts, CancellationToken token);
}

public record DocumentPart(string FileName, string ContentType, BinaryData Content);

/// <summary>
/// Reads a freight operational document (booking confirmation, D/O, B/L,
/// container list, customs release, or a photo of the paperwork) and returns the
/// fields for the Operation Workspace add-job form.
///
/// The shape is enforced by a strict JSON schema on the response rather than
/// scraped out of prose, so there is nothing to parse and nothing to guess at.
/// </summary>
public class DocumentExtractor(IOptions<OpenAiOptions> options, ILogger<DocumentExtractor> log) : IDocumentExtractor
{
    private readonly OpenAiOptions _settings = options.Value;

    private const string Instructions =
        "You are reading a Thai freight-forwarding operational document (booking confirmation, " +
        "delivery order, bill of lading, container list, customs release or a photo of a truck / " +
        "container / paperwork). Extract the operational data. Use \"\" for anything not present — " +
        "never invent values. Dates as DD/MM/YYYY, times as 24h HH:MM. Container numbers are 4 letters " +
        "plus 7 digits. Thai truck plates keep their original format. Company names in the original " +
        "language as printed.";

    private static readonly Dictionary<string, string[]> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IMPORT"] = ["customer", "trucker", "jobCode", "product", "destination", "date", "planTime", "type", "cyYard", "weight", "container", "emptyReturn", "licence", "driver", "contact"],
        ["EXPORT"] = ["customer", "trucker", "booking", "abs", "fclLcl", "plant", "date", "planTime", "type", "cyYard", "returnLoc", "closingDate", "closingTime", "container", "seal", "licence", "driver", "contact"],
        ["DELIVERY"] = ["customer", "wh", "jobNo", "sid", "date", "province", "zip", "pallet", "kgs", "v4", "v6", "v10", "vtr", "cost", "remark"],
    };

    public bool Configured => _settings.ApiKey.Length > 0;

    public async Task<ExtractionResult> ReadAsync(string category, IReadOnlyList<DocumentPart> parts, CancellationToken token)
    {
        if (!Configured)
            return new ExtractionResult(null, "Document reading is not configured — set the OpenAI:ApiKey secret to enable it.", StatusCodes.Status501NotImplemented);

        var keys = Fields.TryGetValue(category, out var wanted) ? wanted : Fields["IMPORT"];

        var content = new List<ChatMessageContentPart>();
        foreach (var part in parts)
        {
            var isPdf = part.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                        || part.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            if (isPdf)
            {
                content.Add(ChatMessageContentPart.CreateFilePart(part.Content, "application/pdf", part.FileName));
                continue;
            }

            var mediaType = ImageType(part.ContentType, part.FileName);
            if (mediaType is null)
                return new ExtractionResult(null, $"{part.FileName} is not a PDF or a supported image", StatusCodes.Status415UnsupportedMediaType);

            content.Add(ChatMessageContentPart.CreateImagePart(part.Content, mediaType));
        }

        content.Add(ChatMessageContentPart.CreateTextPart($"{Instructions} Extract the fields for a {category} job."));

        var client = BuildClient();
        var chatOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "operation_fields",
                jsonSchema: BinaryData.FromString(Schema(keys)),
                jsonSchemaIsStrict: true),
        };

        try
        {
            var completion = await client.CompleteChatAsync([new UserChatMessage(content)], chatOptions, token);
            var text = completion.Value.Content.FirstOrDefault(block => block.Text is { Length: > 0 })?.Text;
            if (string.IsNullOrWhiteSpace(text))
                return new ExtractionResult(null, "No fields were returned for this file.", StatusCodes.Status502BadGateway);

            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text) ?? [];
            var fields = new Dictionary<string, string>();
            foreach (var (key, value) in parsed)
            {
                var raw = (value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()) ?? "";
                raw = raw.Trim();
                // The model was told to answer "" rather than invent; a dash or an
                // N/A means the same thing and is dropped for the same reason.
                if (raw.Length > 0 && raw != "-" && !raw.Equals("n/a", StringComparison.OrdinalIgnoreCase))
                    fields[key] = raw;
            }

            return new ExtractionResult(fields, null, StatusCodes.Status200OK);
        }
        catch (ClientResultException error) when (error.Status == 429)
        {
            return new ExtractionResult(null, "Document reading is busy — try again in a moment.", StatusCodes.Status429TooManyRequests);
        }
        catch (ClientResultException error)
        {
            log.LogWarning(error, "OpenAI refused the extraction request.");
            return new ExtractionResult(null, $"Could not read the file: {error.Message}", StatusCodes.Status502BadGateway);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            log.LogError(error, "Document extraction failed.");
            return new ExtractionResult(null, "Could not read the file.", StatusCodes.Status500InternalServerError);
        }
    }

    private ChatClient BuildClient()
    {
        var credential = new ApiKeyCredential(_settings.ApiKey);
        if (_settings.Endpoint.Length == 0) return new ChatClient(_settings.Model, credential);
        return new ChatClient(_settings.Model, credential, new OpenAIClientOptions { Endpoint = new Uri(_settings.Endpoint) });
    }

    /// <summary>Every field is a required string, so the model has to decide about each one.</summary>
    private static string Schema(IReadOnlyList<string> keys)
    {
        var properties = string.Join(",", keys.Select(key => $"{JsonSerializer.Serialize(key)}:{{\"type\":\"string\"}}"));
        var required = string.Join(",", keys.Select(key => JsonSerializer.Serialize(key)));
        return $"{{\"type\":\"object\",\"properties\":{{{properties}}},\"required\":[{required}],\"additionalProperties\":false}}";
    }

    private static string? ImageType(string contentType, string fileName)
    {
        var probe = contentType + " " + fileName;
        if (probe.Contains("jpg", StringComparison.OrdinalIgnoreCase) || probe.Contains("jpeg", StringComparison.OrdinalIgnoreCase)) return "image/jpeg";
        if (probe.Contains("png", StringComparison.OrdinalIgnoreCase)) return "image/png";
        if (probe.Contains("gif", StringComparison.OrdinalIgnoreCase)) return "image/gif";
        if (probe.Contains("webp", StringComparison.OrdinalIgnoreCase)) return "image/webp";
        return null;
    }
}
