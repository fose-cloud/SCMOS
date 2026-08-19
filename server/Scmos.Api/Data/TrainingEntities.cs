namespace Scmos.Api.Data;

/// <summary>
/// A driver, as somebody who can be trained.
///
/// Deliberately not the same row as <see cref="SupplierDriver"/>. That one
/// belongs to a carrier's fleet record and only exists for drivers a carrier
/// has registered; training has to cover people who are not in that register at
/// all — a replacement sent for one job, somebody a customer supplied. Tying
/// the two would have meant either refusing to record their training or
/// inventing fleet rows for drivers no carrier claims.
///
/// <see cref="SupplierId"/> is therefore optional. When it is set the driver is
/// one of that carrier's, and their portal may maintain the record; when it is
/// null only LESCHACO can.
/// </summary>
public class Driver
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>
    /// National ID or licence number — whatever the certificate is issued
    /// against. This is what stops the same person appearing twice under two
    /// spellings of their name, so it is the value the register is searched by.
    /// </summary>
    public string DriverIdNo { get; set; } = "";

    public string Phone { get; set; } = "";

    /// <summary>The carrier this driver runs for, when they run for one.</summary>
    public int? SupplierId { get; set; }

    /// <summary>
    /// A photograph of the driver, so somebody checking a certificate at a gate
    /// can see they are looking at the right person. Stored like every other
    /// file — a row in the document register with the blob behind it — rather
    /// than as a loose path, so access and retention answer the same way for it
    /// as for everything else.
    /// </summary>
    public long? PhotoDocumentId { get; set; }

    /// <summary>Set false rather than deleting; the training history stays readable.</summary>
    public bool Active { get; set; } = true;

    public string Note { get; set; } = "";

    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A course that can be taken, and how long it usually lasts.</summary>
public class TrainingCourse
{
    public int Id { get; set; }

    /// <summary>Short code the team uses — DEFDRIVE, SAFEIND, DG.</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>
    /// How long a certificate is normally good for. A customer may override it,
    /// and the record itself carries the real expiry date, so this only ever
    /// suggests a date when somebody is keying one in.
    /// </summary>
    public int ValidMonths { get; set; } = 12;

    public bool Active { get; set; } = true;
    public string Note { get; set; } = "";
}

/// <summary>
/// What one customer demands before a driver may run their work.
///
/// Kept per customer because they genuinely differ: one asks for defensive
/// driving and a site induction, another adds dangerous goods handling. A
/// single global list would either refuse drivers nobody needed to refuse, or
/// let through drivers a customer would not accept on site.
/// </summary>
public class CustomerTrainingRequirement
{
    public int Id { get; set; }

    /// <summary>As the register spells it — the same string jobs carry.</summary>
    public string Customer { get; set; } = "";

    public int CourseId { get; set; }

    /// <summary>Overrides the course's own validity when this customer is stricter.</summary>
    public int? ValidMonths { get; set; }

    /// <summary>
    /// False for training a customer asks for but does not enforce. Only
    /// mandatory requirements can make a driver ineligible.
    /// </summary>
    public bool Mandatory { get; set; } = true;

    public string Note { get; set; } = "";
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One certificate, as issued.
///
/// Rows are never updated when training is renewed — a new row is written and
/// the old one stays. The current state of a course for a driver is simply its
/// most recent row, and everything before it is the history an auditor asks
/// for: when they were first trained, when it lapsed, when it was renewed.
/// Overwriting would answer today's question and destroy every earlier one.
/// </summary>
public class DriverTraining
{
    public long Id { get; set; }

    public int DriverId { get; set; }
    public int CourseId { get; set; }

    /// <summary>
    /// The customer this certificate was taken for, when it is site-specific.
    /// Empty means it counts for every customer that requires the course.
    /// </summary>
    public string Customer { get; set; } = "";

    /// <summary>dd/MM/yyyy, as the rest of the register writes dates.</summary>
    public string TrainingDate { get; set; } = "";
    public string ExpiryDate { get; set; } = "";

    public string CertificateNo { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Remark { get; set; } = "";

    /// <summary>
    /// The stored certificate, if one was attached. A row without it is still a
    /// record — an operator keying last month's paperwork should not be blocked
    /// on finding the scan — but the verification screen can list what is
    /// missing evidence.
    /// </summary>
    public long? DocumentId { get; set; }

    /// <summary>Who recorded it. A carrier's own portal writes here too.</summary>
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Set when a record was keyed in error. Not a delete: the row stays and
    /// stops counting, with the reason attached, because a certificate that was
    /// entered and withdrawn is itself something an audit wants to see.
    /// </summary>
    public bool Voided { get; set; }
    public string VoidReason { get; set; } = "";
    public string VoidedBy { get; set; } = "";
}
