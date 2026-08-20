namespace Scmos.Api.Data;

/// <summary>
/// Asking carriers what a journey would cost.
///
/// This is the request side of the rate book. <see cref="RateLane"/> and
/// <see cref="RatePrice"/> hold what a carrier has already agreed; this holds
/// the question that was put to them and the figures that came back, which
/// until now lived in one workbook — a sheet per month, fifty-nine inquiries in
/// August alone — where it could be counted by nobody and searched by whoever
/// had the file open.
///
/// The shape is the workbook's own, because the workbook is the process: one
/// numbered inquiry from one requestor for one customer, and under it as many
/// lanes as that customer asked about. Eleven on a single inquiry is normal.
/// Flattening it into one row per lane, as a naive import would, loses the fact
/// that those eleven are one conversation.
/// </summary>
public class RateInquiry
{
    public long Id { get; set; }

    /// <summary>
    /// The running number inside its month, as the sheet writes it. Not the key
    /// — the key is <see cref="Id"/> — because the workbook restarts at 1 every
    /// month and two inquiries a year apart legitimately share a number.
    /// </summary>
    public int Number { get; set; }

    /// <summary>dd/MM/yyyy, as every other date in the register is written.</summary>
    public string InquiredOn { get; set; } = "";

    /// <summary>Who wants the price. The sheet keeps the full name with its nickname.</summary>
    public string Requestor { get; set; } = "";

    /// <summary>The staff id behind the name, so "my inquiries" is answerable.</summary>
    public string RequestorId { get; set; } = "";

    public string Customer { get; set; } = "";

    /// <summary>
    /// The diesel band the figures are quoted against.
    ///
    /// Every price block in the workbook is headed "Rate Base on Fuel …" and it
    /// is the one piece of context that makes an old quote readable: the same
    /// lane at 29 baht and at 34 baht are different numbers, and a figure with
    /// no band attached cannot be compared to anything later.
    /// </summary>
    public string FuelBand { get; set; } = "";

    /// <summary>Open, Quoted or Closed — see RateInquiryStatus.</summary>
    public string Status { get; set; } = "";

    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One journey inside an inquiry — a row of the sheet.
///
/// Which carriers were asked is kept as the sheet keeps it, a list of names in
/// one field, rather than as rows against the supplier register. The names on
/// those sheets are working shorthand ("SANGJA,SSL,PHURADA,DGT,PK,SHORE") and
/// forcing each one to resolve to a registered supplier before the inquiry
/// could be saved would mean refusing to record a real question because a
/// spelling had not been reconciled yet.
/// </summary>
public class RateInquiryLane
{
    public long Id { get; set; }
    public long InquiryId { get; set; }

    public string FromPlace { get; set; } = "";
    public string ToPlace { get; set; } = "";

    /// <summary>The province, which is how these lanes are grouped and compared.</summary>
    public string County { get; set; } = "";

    /// <summary>Carriers asked, comma separated, in the sheet's own shorthand.</summary>
    public string Carriers { get; set; } = "";

    /// <summary>Full container load, less than container load, or both.</summary>
    public bool Fcl { get; set; }
    public bool Lcl { get; set; }

    public string Remark { get; set; } = "";
}

/// <summary>
/// One figure: a lane, a vehicle, a price.
///
/// A row rather than twenty-four columns. The sheet has a column per vehicle and
/// fills three or four of them, so as columns it is a table that is ninety
/// percent empty and has to be widened every time a new vehicle appears. As rows
/// an unpriced vehicle is simply absent, which is also how the rate book already
/// stores its prices — and how the quotation screen already reads them.
/// </summary>
public class RateInquiryPrice
{
    public long Id { get; set; }
    public long LaneId { get; set; }

    /// <summary>A code from RateVehicles.All. Refused if it is not one.</summary>
    public string Vehicle { get; set; } = "";

    public int Price { get; set; }
}
