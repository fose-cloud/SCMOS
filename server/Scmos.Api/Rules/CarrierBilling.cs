namespace Scmos.Api.Rules;

public static class BillingCaseStatus
{
    public const string WaitingCarrierSubmission = "WAITING_CARRIER_SUBMISSION";
    public const string Draft = "DRAFT";
    public const string Validated = "VALIDATED";
    public const string Blocked = "BLOCKED";
}

public static class BillingInvoiceStatus
{
    public const string Draft = "DRAFT";
    public const string Validated = "VALIDATED";
    public const string Blocked = "BLOCKED";
}

public static class BillingValidationCategory
{
    public const string Pass = "PASS";
    public const string Warning = "WARNING";
    public const string Exception = "EXCEPTION";
    public const string Blocked = "BLOCKED";
}

public record BillingRuleResult(string Code, string Category, bool Blocking, string Message,
    decimal? Expected = null, decimal? Actual = null);

public static class BillingValidationRules
{
    public static BillingRuleResult Document(bool present, bool blocking) => present
        ? new("DOCUMENT_PRESENT", BillingValidationCategory.Pass, false, "Required document is present")
        : new("MISSING_REQUIRED_DOCUMENT", blocking ? BillingValidationCategory.Blocked : BillingValidationCategory.Exception, blocking, "Required document is missing");
    public static BillingRuleResult Rate(int matches, decimal? expected, decimal claimed) => matches switch {
        0 => new("CONTRACT_RATE_NOT_FOUND", BillingValidationCategory.Blocked, true, "No contracted rate applies"),
        > 1 => new("MULTIPLE_RATE_MATCH", BillingValidationCategory.Blocked, true, "Multiple contracted rates apply"),
        _ when expected != claimed => new("CONTRACT_RATE_MISMATCH", BillingValidationCategory.Exception, true, "Claim does not match contracted rate", expected, claimed),
        _ => new("CONTRACT_RATE_MATCH", BillingValidationCategory.Pass, false, "Claim matches contracted rate", expected, claimed),
    };
    public static BillingRuleResult Charge(bool approved) => approved
        ? new("ADDITIONAL_CHARGE_APPROVED", BillingValidationCategory.Pass, false, "Additional charge is approved")
        : new("UNAPPROVED_ADDITIONAL_CHARGE", BillingValidationCategory.Blocked, true, "Additional charge is not approved");
    public static BillingRuleResult Tax(decimal? expected, decimal actual) => expected is null
        ? new("TAX_RULE_NOT_FOUND", BillingValidationCategory.Blocked, true, "No effective tax rule applies")
        : expected != actual
            ? new("TAX_MISMATCH", BillingValidationCategory.Exception, true, "Tax does not match configured rule", expected, actual)
            : new("TAX_MATCH", BillingValidationCategory.Pass, false, "Tax matches configured rule", expected, actual);
    public static BillingRuleResult Duplicate(bool sameNumber, bool alreadyBilled, bool risk) => alreadyBilled
        ? new("JOB_ALREADY_BILLED", BillingValidationCategory.Blocked, true, "Job is already fully billed")
        : sameNumber ? new("DUPLICATE_INVOICE_NUMBER", BillingValidationCategory.Blocked, true, "Invoice number is duplicated")
        : risk ? new("POSSIBLE_DUPLICATE_BILLING", BillingValidationCategory.Warning, false, "Job, amount and date match another claim")
        : new("DUPLICATE_CHECK_PASSED", BillingValidationCategory.Pass, false, "No duplicate billing was found");
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
