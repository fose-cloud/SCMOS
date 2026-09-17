using System.ClientModel;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Reads the container number off a photograph a driver posted in a LINE group.
///
/// <para>
/// Asked for on 16 Sep 2026, taken out on the 17th when every photo was read
/// and answered, and asked for again the same afternoon in one shape only:
/// photos of the door and the seal followed by "รถถึงคลังแล้ว 13.39 น.". The
/// photo is fetched from LINE with the channel access token (LINE keeps
/// message content only for a while) and shown to the same model the
/// document reader uses, which answers in a fixed shape that
/// <see cref="LineImageReading"/> checks digit by digit. Nothing is kept:
/// the reading goes on the row, the bytes go.
/// </para>
///
/// <para>
/// Nothing here writes to a job. What comes back joins the text it belongs
/// with (<see cref="LinePhotoPairing"/>) and waits for a person like every
/// message does — a model's reading of a muddy door is a suggestion, and the
/// check digit only says it is a <i>possible</i> container, not the right one.
/// </para>
/// </summary>
public interface ILineImageReader
{
    /// <summary>Whether photos can be read at all: the switch, the token and the model key are all set.</summary>
    bool Configured { get; }

    /// <summary>Why not, when <see cref="Configured"/> is false, for the status endpoint.</summary>
    string Missing { get; }

    /// <summary>Fetches and reads one photo. Never throws for a bad photo; throws for a broken service.</summary>
    Task<LineImageReader.Result> ReadAsync(LineEvent row, CancellationToken token);
}

public class LineImageReader(
    IHttpClientFactory factory,
    IConfiguration config,
    IOptions<OpenAiOptions> openAi,
    ILogger<LineImageReader> log) : ILineImageReader
{
    /// <summary>The switch. Off by default: a photo read is a model call somebody pays for.</summary>
    public const string ReadImagesKey = "Line:ReadImages";

    /// <summary>Where LINE serves a message's content.</summary>
    private const string ContentUrl = "https://api-data.line.me/v2/bot/message/{0}/content";

    /// <summary>The one thing this file will not do: upload a whole video to a chat model.</summary>
    private const long MaxBytes = 12 * 1024 * 1024;

    /// <param name="Reading">What the model read, checked.</param>
    /// <param name="Failure">Empty when the photo was read; otherwise why it could not be, in Thai, for the row.</param>
    public record Result(LineImageReading.Reading Reading, string Failure);

    public bool Configured => Missing.Length == 0;

    public string Missing =>
        !config.GetValue(ReadImagesKey, false) ? "ปิดอยู่ — ตั้ง Line__ReadImages เป็น true เมื่อพร้อม"
        : string.IsNullOrWhiteSpace(config[LineNotifier.TokenKey]) ? "ยังไม่ได้ตั้ง Line__ChannelAccessToken"
        : openAi.Value.ApiKey.Length == 0 ? "ยังไม่ได้ตั้ง OpenAI__ApiKey"
        : "";

    public async Task<Result> ReadAsync(LineEvent row, CancellationToken token)
    {
        if (!Configured) return new Result(LineImageReading.Reading.Empty, Missing);

        var (bytes, mediaType) = await FetchAsync(row, token);
        if (bytes is null) return new Result(LineImageReading.Reading.Empty, mediaType);

        var answer = await AskAsync(bytes, mediaType, token);
        var reading = LineImageReading.Read(answer);
        log.LogInformation("LINE photo {Id}: {Valid} container(s) read, {Rejected} rejected",
            row.Id, reading.Valid.Count, reading.Rejected.Count);
        return new Result(reading, "");
    }

    /// <summary>
    /// The bytes from LINE, or a reason. A photo posted from another app can
    /// carry its own URL (contentProvider "external"); a photo taken in LINE
    /// is fetched from LINE with the token.
    /// </summary>
    private async Task<(byte[]? Bytes, string TypeOrReason)> FetchAsync(LineEvent row, CancellationToken token)
    {
        var client = factory.CreateClient(LineNotifier.ClientName);
        client.Timeout = TimeSpan.FromSeconds(60);

        using var request = new HttpRequestMessage(HttpMethod.Get, string.Format(ContentUrl, row.LineMessageId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config[LineNotifier.TokenKey]);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "LINE ไม่มีรูปนี้แล้ว (เก็บไว้ชั่วคราวเท่านั้น)");
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            return (null, "LINE ไม่รับ Line__ChannelAccessToken — ตรวจค่าใน Portal");
        if (!response.IsSuccessStatusCode)
            return (null, $"LINE ตอบ {(int)response.StatusCode} ตอนขอรูป");

        var type = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return (null, $"ไม่ใช่รูปภาพ ({type})");
        if (response.Content.Headers.ContentLength is > MaxBytes)
            return (null, "รูปใหญ่เกินไป");

        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, token);
        if (buffer.Length > MaxBytes) return (null, "รูปใหญ่เกินไป");
        return (buffer.ToArray(), type);
    }

    private async Task<string?> AskAsync(byte[] bytes, string mediaType, CancellationToken token)
    {
        var settings = openAi.Value;
        var credential = new ApiKeyCredential(settings.ApiKey);
        var client = settings.Endpoint.Length == 0
            ? new ChatClient(settings.Model, credential)
            : new ChatClient(settings.Model, credential, new OpenAIClientOptions { Endpoint = new Uri(settings.Endpoint) });

        var content = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(bytes), mediaType),
            ChatMessageContentPart.CreateTextPart(LineImageReading.Instructions),
        };
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "container_numbers",
                jsonSchema: BinaryData.FromString(LineImageReading.Schema),
                jsonSchemaIsStrict: true),
        };

        var completion = await client.CompleteChatAsync([new UserChatMessage(content)], options, token);
        return completion.Value.Content.FirstOrDefault(block => block.Text is { Length: > 0 })?.Text;
    }
}
