using System.Security.Cryptography;
using System.Text;

namespace Scmos.Api.Rules;

/// <summary>
/// The links evaluators outside SCMOS answer through (1 Oct 2026, Annual Evaluation Phases 6–7), and what an answer
/// sheet must hold. Pure.
///
/// <para>
/// A link carries a 256-bit random token, made here and shown once to the admin who copies it; the database keeps only
/// its SHA-256, so neither a read of the table nor a log can be turned back into a working link. The token travels in
/// the page's fragment (<c>/evaluation#token</c>), which a browser never sends to a server, and from there in a header.
/// Nothing is emailed — SCMOS sends no mail; the admin sends the link from their own Outlook.
/// </para>
/// </summary>
public static class EvaluationInvitations
{
    /// <summary>A new token: 32 random bytes, base64url, 43 characters.</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Whether text could be a token at all, before anything is looked up.</summary>
    public static bool LooksLikeToken(string? token) =>
        token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>The token's SHA-256 in lower-case hex — the only form stored.</summary>
    public static string HashOf(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>The rate-limit bucket a request falls in: its token's (hash prefix), else one shared bucket.</summary>
    public static string PartitionOf(string? token) => LooksLikeToken(token) ? "eval:" + HashOf(token!)[..16] : "eval:anonymous";

    public static int AllowanceOf(string partition) => partition == "eval:anonymous" ? 20 : 60;

    public const string Expired = "expired";

    /// <summary>Where an invitation stands now: its stored state, or expired when its time has passed and it was never answered.</summary>
    public static string StateOf(string status, DateTimeOffset expiresAt, DateTimeOffset now) =>
        status is AnnualEvaluationRules.InvitationSubmitted or AnnualEvaluationRules.InvitationRevoked || expiresAt > now ? status : Expired;

    /// <summary>A question as an evaluator is asked it.</summary>
    public sealed record Asked(string Code, bool Required, int CommentRequiredAtOrBelow);

    /// <summary>One answer: a rating 1–5, or N/A (no rating), and a comment.</summary>
    public sealed record Answer(string? Code, int? Rating, bool NotApplicable, string? Comment);

    public const int MaxComment = 2000;

    /// <summary>
    /// What is wrong with an answer sheet, in words the page shows; empty when it may be saved. N/A is an answer — it is
    /// not a rating of nought — so a required question answered N/A is answered. A rating at or below the threshold needs
    /// a comment.
    /// </summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<Asked> asked, IReadOnlyList<Answer> answers, string? comment)
    {
        var problems = new List<string>();
        var byCode = new Dictionary<string, Answer>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            var code = (answer.Code ?? "").Trim();
            if (asked.All(question => question.Code != code)) { problems.Add($"ไม่มีคำถาม {code} ในแบบประเมินนี้"); continue; }
            if (!byCode.TryAdd(code, answer)) problems.Add($"ตอบคำถาม {code} ซ้ำ");
        }
        foreach (var question in asked)
        {
            byCode.TryGetValue(question.Code, out var answer);
            var rated = answer is { NotApplicable: false, Rating: not null };
            if (answer is null || (!answer.NotApplicable && answer.Rating is null))
            {
                if (question.Required) problems.Add($"ยังไม่ได้ตอบคำถาม {question.Code}");
                continue;
            }
            if (answer.NotApplicable && answer.Rating is not null) problems.Add($"คำถาม {question.Code}: เลือกคะแนนหรือ N/A อย่างใดอย่างหนึ่ง");
            if (rated && answer.Rating is < 1 or > 5) problems.Add($"คำถาม {question.Code}: คะแนนต้องอยู่ระหว่าง 1–5");
            if (rated && answer.Rating <= question.CommentRequiredAtOrBelow && string.IsNullOrWhiteSpace(answer.Comment))
                problems.Add($"คำถาม {question.Code}: ให้คะแนน {answer.Rating} ต้องระบุเหตุผล");
            if ((answer.Comment ?? "").Length > MaxComment) problems.Add($"คำถาม {question.Code}: ความเห็นยาวเกินไป");
        }
        if ((comment ?? "").Length > MaxComment * 2) problems.Add("ความเห็นรวมยาวเกินไป");
        return problems;
    }
}
