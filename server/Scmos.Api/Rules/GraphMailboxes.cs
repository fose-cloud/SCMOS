namespace Scmos.Api.Rules;

/// <summary>
/// Which mailboxes this deployment is permitted to read at all.
///
/// <para>
/// <c>Mail.Read</c> as an application permission reads <b>every</b> mailbox in
/// the tenant. Exchange Online RBAC for Applications is what actually narrows
/// that, and it is an Azure change nobody here can make. This is the second
/// fence: a list in configuration that the code checks before it asks Graph for
/// anything. It does not replace the RBAC scoping — a wrong answer here still
/// gets refused by Exchange — but it means a mistaken row in the
/// <c>mailboxes</c> table cannot reach a mailbox the deployment never approved.
/// </para>
///
/// <para>
/// <b>An empty list approves nothing.</b> The tempting reading — no list means
/// no restriction — is how a directory of every mailbox in a forwarding company
/// becomes readable because somebody forgot an app setting. SCMOS already
/// refuses an unknown sign-in rather than giving it a default role, and this is
/// the same answer to the same shape of question.
/// </para>
/// </summary>
public static class GraphMailboxes
{
    /// <summary>
    /// One spelling of an address, so two spellings of one mailbox compare equal.
    ///
    /// Lower-cased: the local part of an address is case-sensitive by RFC and
    /// case-insensitive in Exchange, and Exchange is what serves these.
    /// </summary>
    public static string Normalise(string? address) =>
        (address ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// The approved list as configured — <c>Graph__Mailboxes</c>.
    ///
    /// Split on commas, semicolons and whitespace, because an app setting is
    /// typed by a person and the separator they reach for is not predictable.
    /// No address contains any of them unquoted, so nothing is lost by accepting
    /// all of them. Duplicates are dropped; order is kept, so the readiness
    /// report reads back the way it was written.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return [];

        var found = new List<string>();
        foreach (var part in configured.Split([',', ';', ' ', '\t', '\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var one = Normalise(part);
            if (one.Length == 0 || found.Contains(one, StringComparer.Ordinal)) continue;
            found.Add(one);
        }
        return found;
    }

    /// <summary>
    /// Whether this deployment may read that mailbox.
    ///
    /// Both sides are normalised rather than trusting the list to have been
    /// built by <see cref="Parse"/>, because the one call site that forgets is
    /// the one that silently approves nothing — or, worse, is later "fixed" by
    /// loosening the comparison.
    /// </summary>
    public static bool IsApproved(string? address, IReadOnlyList<string>? approved)
    {
        var one = Normalise(address);
        if (one.Length == 0 || approved is null) return false;

        foreach (var entry in approved)
            if (string.Equals(Normalise(entry), one, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// Whether an entry looks like a mailbox address, for the readiness report.
    ///
    /// Not validation — Graph decides what exists. This exists so that a typo,
    /// or a wildcard somebody assumed would work, is <i>reported</i> rather than
    /// quietly matching nothing and looking like a mailbox that never gets mail.
    /// </summary>
    public static bool LooksLikeAddress(string? address)
    {
        var one = Normalise(address);
        if (one.Length == 0 || one.Any(char.IsWhiteSpace)) return false;

        // '*' and '?' are legal in an address by RFC and never appear in one
        // here. Somebody who types either meant a wildcard, and this list does
        // not match wildcards — so a domain somebody thought they had approved
        // is reported, rather than matching nothing in silence.
        if (one.Contains('*') || one.Contains('?')) return false;

        var at = one.IndexOf('@');
        if (at <= 0 || at != one.LastIndexOf('@') || at == one.Length - 1) return false;

        var domain = one[(at + 1)..];
        return domain.Contains('.', StringComparison.Ordinal)
            && !domain.StartsWith('.') && !domain.EndsWith('.');
    }
}
