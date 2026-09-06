using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Text">The draft, or null when there is none to offer.</param>
/// <param name="Error">Why not, in words a manager can act on.</param>
public record CommentaryResult(string? Text, string? Error, int Status);

/// <summary>
/// Drafts the management summary that sits under the figures.
///
/// <para>
/// The assistant is given the report's own numbers and nothing else, and every
/// number it writes back is checked against that list by
/// <see cref="ReportCommentary"/> before it is offered. A draft citing a figure
/// nobody measured is discarded rather than shown, because the whole risk of
/// this feature is a sentence that reads exactly like the true ones beside it.
/// </para>
///
/// <para>
/// It is also a draft and stays one. The text goes into a box the person can
/// edit, and nothing writes it into a report unattended — which is the same
/// arrangement the assistant already has everywhere else in this system: it may
/// read and propose, and a person decides.
/// </para>
/// </summary>
public class ReportWriterService(IOptions<OpenAiOptions> options, ILogger<ReportWriterService> log)
{
    private readonly OpenAiOptions _settings = options.Value;

    public bool Configured => _settings.ApiKey.Trim().Length > 0;

    public async Task<CommentaryResult> DraftAsync(MonthlyReportView report, CancellationToken token)
    {
        if (!Configured)
        {
            return new CommentaryResult(null,
                "ยังไม่ได้ตั้งค่าผู้ช่วย AI — เขียนสรุปเองได้ในช่องด้านล่าง", 503);
        }

        var facts = ReportCommentary.Facts(report);

        try
        {
            var client = _settings.Endpoint.Length == 0
                ? new ChatClient(_settings.Model, new System.ClientModel.ApiKeyCredential(_settings.ApiKey))
                : new ChatClient(_settings.Model, new System.ClientModel.ApiKeyCredential(_settings.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(_settings.Endpoint) });

            var answer = await client.CompleteChatAsync(
                [
                    new SystemChatMessage(ReportCommentary.Instruction),
                    new UserChatMessage(facts),
                ],
                new ChatCompletionOptions
                {
                    // Short, because it is three to five sentences and a longer
                    // budget only buys a preamble the rule then rejects.
                    MaxOutputTokenCount = 400,
                    // Low, not zero: this is prose, and the figures it may use
                    // are fenced by the check rather than by the sampler.
                    Temperature = 0.2f,
                },
                token);

            var draft = string.Concat(answer.Value.Content.Select(part => part.Text));
            var (text, refusal) = ReportCommentary.Judge(draft, facts);

            if (text is null)
            {
                // Logged rather than swallowed: a model that keeps inventing
                // figures is a prompt that needs changing, and nobody would
                // ever find that out from the screen's polite refusal.
                log.LogWarning("Report commentary rejected for {Customer} {Month}: {Reason}",
                    report.Customer, report.Month, refusal);
                return new CommentaryResult(null, refusal, 422);
            }

            return new CommentaryResult(text, null, 200);
        }
        catch (System.ClientModel.ClientResultException refused)
        {
            // Logged with the status so an administrator can tell a wrong key
            // from an empty account without asking the person who pressed the
            // button what the message said.
            log.LogWarning(refused, "OpenAI refused the commentary request ({Status})", refused.Status);
            return new CommentaryResult(null,
                ReportCommentary.Explain(refused.Status, refused.Message), 502);
        }
        catch (Exception problem)
        {
            log.LogError(problem, "Report commentary could not be drafted");
            return new CommentaryResult(null, ReportCommentary.Explain(0, null), 502);
        }
    }
}
