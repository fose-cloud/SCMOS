namespace Scmos.Api.Rules;

/// <summary>
/// Which mail is read out of a personal mailbox (4 Oct 2026). Pure, proved by <c>--check-email</c>.
///
/// <para>
/// The department moves Operation, Supervisors and the Assistant Manager onto their Leschaco mailboxes, and SCMOS reads
/// those mailboxes for booking mail. Exchange grants a mailbox whole — it cannot say "only from these senders" — so
/// SCMOS says it: a message in a personal mailbox is read only when its sender is on the list Supervisors keep, a full
/// address each (the user's decision; a domain would let in everybody at a customer). For anyone else the message is
/// never fetched — only its sender is looked at — and nothing of it is kept, not even the subject. A shared mailbox is
/// read as before. A personal mailbox with nobody listed reads nothing.
/// </para>
/// </summary>
public static class MailSenders
{
    /// <summary>The longest note kept beside an address.</summary>
    public const int NoteLength = 200;

    /// <summary>How an address is held and compared: trimmed, lower case, no angle brackets.</summary>
    public static string Normalise(string? address) =>
        (address ?? "").Trim().Trim('<', '>').Trim().ToLowerInvariant();

    /// <summary>One full address: something@domain.tld, no spaces, one @ — never a bare domain or a pattern.</summary>
    public static bool IsAddress(string? address)
    {
        var text = Normalise(address);
        var at = text.IndexOf('@');
        return text.Length is > 3 and <= 320 && at > 0 && at == text.LastIndexOf('@') && at < text.Length - 1
            && text[(at + 1)..].Contains('.') && !text.EndsWith('.') && !text.Any(char.IsWhiteSpace)
            && !text.Contains('*') && !text.Contains(',') && !text.Contains(';');
    }

    /// <summary>Whether a mailbox is somebody's own: it names an owner.</summary>
    public static bool Personal(string? ownerOperatorId) => !string.IsNullOrWhiteSpace(ownerOperatorId);

    /// <summary>
    /// Whether a message is read: always from a shared mailbox; from a personal one only when the sender is listed.
    /// An unknown sender — a message whose sender could not be read — is not listed.
    /// </summary>
    public static bool Reads(string? ownerOperatorId, string? sender, IReadOnlySet<string> allowed) =>
        !Personal(ownerOperatorId) || (Normalise(sender) is { Length: > 0 } from && allowed.Contains(from));
}
