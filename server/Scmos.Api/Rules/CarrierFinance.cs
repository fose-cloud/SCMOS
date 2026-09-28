namespace Scmos.Api.Rules;

public static class FinanceStatus
{
    public const string Queued = "QUEUED";
    public const string Processing = "PROCESSING";
    public const string Retrying = "RETRYING";
    public const string Submitted = "SUBMITTED";
    public const string Accepted = "ACCEPTED";
    public const string Rejected = "REJECTED";
    public const string Paid = "PAID";
    public const string Closed = "CLOSED";
    public const string Failed = "FAILED";

    public static readonly string[] ReconciliationStates = [Accepted, Rejected, Paid, Closed];
}

public static class OutboxStatus
{
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    public const string Retry = "RETRY";
    public const string Completed = "COMPLETED";
    public const string Dead = "DEAD";
}

public static class FinanceEvents
{
    public const string SubmissionRequested = "FINANCE_SUBMISSION_REQUESTED";
}

public static class FinanceRetry
{
    // A bounded durable retry schedule: immediate attempt, then 1, 5, 15,
    // 60 and 240 minutes. After that an operator must deliberately retry.
    private static readonly int[] Minutes = [1, 5, 15, 60, 240];

    public static DateTimeOffset? Next(int completedAttempts, DateTimeOffset now) =>
        completedAttempts >= 1 && completedAttempts <= Minutes.Length
            ? now.AddMinutes(Minutes[completedAttempts - 1])
            : null;
}

public static class FinanceReconciliation
{
    public static bool CanApply(string current, string next) => next switch
    {
        FinanceStatus.Accepted => current is FinanceStatus.Submitted or FinanceStatus.Processing or FinanceStatus.Accepted,
        FinanceStatus.Rejected => current is FinanceStatus.Submitted or FinanceStatus.Processing or FinanceStatus.Rejected,
        FinanceStatus.Paid => current is FinanceStatus.Accepted or FinanceStatus.Submitted or FinanceStatus.Paid,
        FinanceStatus.Closed => current is FinanceStatus.Paid or FinanceStatus.Closed,
        _ => false,
    };

    public static string InvoiceStatus(string financeStatus) => financeStatus switch
    {
        FinanceStatus.Rejected => BillingInvoiceStatus.FinanceRejected,
        FinanceStatus.Paid => BillingInvoiceStatus.Paid,
        FinanceStatus.Closed => BillingInvoiceStatus.Closed,
        _ => BillingInvoiceStatus.FinanceProcessing,
    };
}
