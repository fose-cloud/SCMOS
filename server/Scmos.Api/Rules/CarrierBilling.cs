namespace Scmos.Api.Rules;

public static class BillingCaseStatus
{
    public const string WaitingCarrierSubmission = "WAITING_CARRIER_SUBMISSION";
    public const string Draft = "DRAFT";
}

public static class BillingInvoiceStatus
{
    public const string Draft = "DRAFT";
}

public static class BillingSlaState
{
    public const string ConfigurationMissing = "CONFIGURATION_MISSING";
    public const string OnTrack = "ON_TRACK";
    public const string DueSoon = "DUE_SOON";
    public const string DueToday = "DUE_TODAY";
    public const string Overdue = "OVERDUE";

    public static string Of(DateOnly today, DateOnly? due)
    {
        if (due is null) return ConfigurationMissing;
        var days = due.Value.DayNumber - today.DayNumber;
        if (days < 0) return Overdue;
        if (days == 0) return DueToday;
        if (days == 1) return DueSoon;
        return OnTrack;
    }
}

public static class BillingEligibility
{
    public static bool IsEligible(string jobStatus, string assignmentOutcome) =>
        string.Equals(jobStatus, JobStatus.Completed, StringComparison.OrdinalIgnoreCase)
        && string.Equals(assignmentOutcome, CarrierAssignment.Confirmed, StringComparison.OrdinalIgnoreCase);
}
