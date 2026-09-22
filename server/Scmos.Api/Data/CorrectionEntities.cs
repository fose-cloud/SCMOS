namespace Scmos.Api.Data;

/// <summary>
/// One cell the system proposes to change, waiting for the job's owner.
///
/// Asked for on 22 Sep 2026: "AI แก้ข้อมูลที่ไม่ตรงกับ DATA … ที่ทำ Dropdown"
/// and "เมื่อแก้ไขเสร็จแล้ว จะต้องรอ Operation กด Approve เหมือนไลน์". A
/// dropdown column — customer, trucker, type, category, status — may only
/// hold a value from its list, but a value typed or imported before the list
/// existed, or spelled another way, sits there as its own thing: 1X40 REEFER
/// beside 1X40' RF, SJ beside Sangja Transport. The rules that know the
/// lists propose the list's spelling; nothing is written until the person
/// who owns the job says so, on the job, the way a haulier's LINE message
/// is approved. A row here is a proposal and a record of what became of it,
/// never the change itself.
/// </summary>
public class JobCorrection
{
    public long Id { get; set; }

    /// <summary>The run that proposed it — yyyyMMddHHmmss — so one pass can be told from another.</summary>
    public string Batch { get; set; } = "";

    public string JobKey { get; set; } = "";
    /// <summary>The job's number as the drawer shows it; display only.</summary>
    public string JobCode { get; set; } = "";
    /// <summary>Who owned the job when this was proposed. Whose approval is asked for is judged on the job as it is now, not on this.</summary>
    public string OwnerId { get; set; } = "";

    /// <summary>type · customer · trucker · cat · status — a dropdown column of the job's JSON.</summary>
    public string Field { get; set; } = "";
    /// <summary>The cell as it was. Apply refuses when the cell no longer says this.</summary>
    public string FromValue { get; set; } = "";
    /// <summary>The list's spelling.</summary>
    public string ToValue { get; set; } = "";
    /// <summary>Which rule proposed it, for grouping: type.canonical, trucker.directory, customer.rotation, cat.case, status.legacy.</summary>
    public string Rule { get; set; } = "";
    /// <summary>Why, in the owner's language.</summary>
    public string Reason { get; set; } = "";

    public string ProposedBy { get; set; } = "";
    public DateTimeOffset ProposedAt { get; set; }

    /// <summary>pending · applied · rejected · stale — see <see cref="CorrectionState"/>.</summary>
    public string State { get; set; } = CorrectionState.Pending;
    public string DecidedBy { get; set; } = "";
    public DateTimeOffset? DecidedAt { get; set; }
    /// <summary>What happened: the owner's note, or why it went stale.</summary>
    public string Note { get; set; } = "";
}

public static class CorrectionState
{
    /// <summary>Waiting for the job's owner.</summary>
    public const string Pending = "pending";
    /// <summary>The owner approved and the cell was written.</summary>
    public const string Applied = "applied";
    /// <summary>The owner said no. The value stays as typed.</summary>
    public const string Rejected = "rejected";
    /// <summary>The cell changed, or the job went, before anybody decided; nothing was written.</summary>
    public const string Stale = "stale";
}
