namespace Scmos.Api.Data;

/// <summary>
/// Somebody else may work my jobs while I am away.
///
/// The whole feature exists because people take leave and the work does not.
/// Without it the choices are to leave a fortnight of shipments unattended, or
/// to hand a colleague an account — which puts their edits under somebody
/// else's name and empties the audit trail of the one fact it exists to hold.
///
/// Three properties make this a delegation rather than a permission change:
///
/// <b>It ends.</b> Both dates are required. A grant with no end is a permanent
/// change of who owns the work, dressed up as a holiday arrangement, and the
/// person who set it will not remember to remove it. Expiry needs nothing to
/// run — the dates are compared to today whenever the question is asked.
///
/// <b>It is visible.</b> The owner sees every grant they have made and can end
/// one early; an administrator can see all of them. A quiet grant is
/// indistinguishable from an account being shared.
///
/// <b>It is attributed.</b> Work done under a delegation is still signed by the
/// person who did it. The audit answers "who changed this" with the delegate's
/// name, not the owner's, which is the point of not sharing the account.
/// </summary>
public class JobDelegation
{
    public long Id { get; set; }

    /// <summary>Whose jobs these are — the staff id the jobs carry as owner.</summary>
    public string OwnerId { get; set; } = "";

    /// <summary>Who may work them while the grant is live.</summary>
    public string DelegateId { get; set; } = "";

    /// <summary>dd/MM/yyyy, inclusive at both ends, as the register writes dates.</summary>
    public string FromDate { get; set; } = "";
    public string ToDate { get; set; } = "";

    /// <summary>
    /// Why — "ลาพักร้อน 5–12 ส.ค." Required, because a grant of write access to
    /// somebody else's work with no stated reason is the one an auditor asks
    /// about and nobody can answer.
    /// </summary>
    public string Reason { get; set; } = "";

    /// <summary>
    /// Ended before its date. Not deleted: a grant that existed and was
    /// withdrawn is part of the answer to who could have edited what, and when.
    /// </summary>
    public bool Revoked { get; set; }
    public string RevokedBy { get; set; } = "";
    public DateTimeOffset? RevokedAt { get; set; }

    public string CreatedBy { get; set; } = "";

    /// <summary>
    /// The staff id of whoever arranged it, which is not always the owner —
    /// a supervisor may arrange cover for somebody who could not.
    ///
    /// Stored rather than worked out from <see cref="CreatedBy"/>, which holds
    /// an email: asking whether a signature contains somebody's name answers a
    /// question about spelling, not about who did it. Empty on grants written
    /// before this existed, and read as "the owner", which is what they all
    /// were — nobody else could make one.
    /// </summary>
    public string CreatedById { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
