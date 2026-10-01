namespace Scmos.Api.Data;

/// <summary>
/// One year's EHS audit plan with the truck subcontractors (1 Oct 2026) — the department's workbook
/// "2026 AUDIT planning of EHS with Truck Sub-Contractor", its sign-off boxes kept as the sheet has them.
/// </summary>
public class AuditPlan
{
    public int Id { get; set; }
    public int Year { get; set; }
    public string Title { get; set; } = "";
    public string PreparedBy { get; set; } = "";
    public string ReviewedBy { get; set; } = "";
    /// <summary>DD/MM/YYYY, as the sheet writes the review date under the reviewer.</summary>
    public string ReviewedDate { get; set; } = "";
    public string SecondReviewedBy { get; set; } = "";
    public string ApprovedBy { get; set; } = "";
    public string Revision { get; set; } = "00";
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One audit on the plan: a subcontractor, where, who audits, and when — a re-audit of a carrier already
/// working, or the audit a new subcontractor is approved by (set from Add New Vendor). See
/// <see cref="Rules.AuditPlanRules"/> for the schedule marks.
/// </summary>
public class AuditPlanItem
{
    public long Id { get; set; }
    public int Year { get; set; }

    /// <summary>re-audit · new</summary>
    public string Kind { get; set; } = Rules.AuditPlanRules.ReAudit;

    /// <summary>The row's number within its section, as the sheet numbers them.</summary>
    public int Sequence { get; set; }

    /// <summary>The supplier register's row, when the company is in it.</summary>
    public int? SupplierId { get; set; }

    /// <summary>The company as written — the register's name when linked.</summary>
    public string Company { get; set; } = "";

    /// <summary>The site audited: LCB, BKK.</summary>
    public string Target { get; set; } = "";

    /// <summary>The auditors, one per line, as the sheet lists them.</summary>
    public string PersonInCharge { get; set; } = "";

    /// <summary>DD/MM/YYYY.</summary>
    public string AuditDate { get; set; } = "";

    /// <summary>fixed · tentative</summary>
    public string Schedule { get; set; } = Rules.AuditPlanRules.Fixed;

    /// <summary>planned · done · postponed · continue · cancelled</summary>
    public string Status { get; set; } = Rules.AuditPlanRules.Planned;

    /// <summary>DD/MM/YYYY — where a postponed audit moved to, or where a continued one carries on.</summary>
    public string NextDate { get; set; } = "";

    public string Remark { get; set; } = "";

    /// <summary>DD/MM/YYYY — the follow-up columns: findings sent, audit report sent.</summary>
    public string FindingSentDate { get; set; } = "";
    public string ReportSentDate { get; set; } = "";

    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Where one line of a new subcontractor's onboarding checklist stands (1 Oct 2026) — the department's
/// thirteen documents, <see cref="Rules.VendorOnboarding"/>. The files are StoredDocuments under the supplier,
/// filed with the line's kind; this row holds what a person decides: Done or Pending, and the remark.
/// </summary>
public class SupplierOnboardingItem
{
    public long Id { get; set; }
    public int SupplierId { get; set; }
    public string Code { get; set; } = "";

    /// <summary>pending · done</summary>
    public string Status { get; set; } = Rules.VendorOnboarding.Pending;

    public string Remark { get; set; } = "";
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
