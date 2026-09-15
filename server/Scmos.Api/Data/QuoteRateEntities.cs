namespace Scmos.Api.Data;

/// <summary>
/// How one vehicle is priced from the distance it travels.
///
/// The team's own card: a rate per kilometre of the outbound journey, a fixed
/// base, a multiplier for the refrigerated variants, and the flat surcharge a
/// dangerous load carries.
///
/// <para>Held here rather than written into the screen because a quoting rule
/// that lives in a browser is a quoting rule each person has their own copy of,
/// and two people quoting one journey differently is the failure this whole
/// register exists to prevent. It also wants tuning: measured against the 13,042
/// prices already in the register, the ×1.5 on a refrigerated truck is high — a
/// 10W RF came out at ×1.24 across thirty journeys where both were quoted — and
/// correcting that should not need a deployment.</para>
/// </summary>
public class QuoteVehicleRate
{
    public int Id { get; set; }

    /// <summary>The register's own vehicle code, so a quote and a job mean the same truck.</summary>
    public string Code { get; set; } = "";

    /// <summary>What the team calls it — "4WH", "20' Reefer".</summary>
    public string Label { get; set; } = "";

    /// <summary>Baht per kilometre of the outbound journey.</summary>
    public int PerKm { get; set; }

    /// <summary>Baht before a wheel turns.</summary>
    public int BaseCharge { get; set; }

    /// <summary>Multiplies the transport cost. 1 for anything not refrigerated.</summary>
    public decimal Chill { get; set; } = 1m;

    /// <summary>Baht added when the load is dangerous goods.</summary>
    public int DangerousGoods { get; set; }

    /// <summary>The order the card is read in, which is the order it was written in.</summary>
    public int Position { get; set; }
}

/// <summary>
/// An extra charge a quotation can carry — waiting time, a night out, a fuel
/// surcharge.
///
/// <para><see cref="Basis"/> is the field that matters. It says how the charge
/// applies — a flat sum, per kilometre, per hour, or a share of the cost — and
/// it is a controlled list rather than the typed unit the older
/// <c>rate_surcharges</c> table carries. A free-text unit column ends up holding
/// "ต่อชั่วโมง", "per hr" and "/hour" for one idea, and then nothing can add
/// them up.</para>
/// </summary>
public class QuoteExtra
{
    public int Id { get; set; }
    public string Label { get; set; } = "";

    /// <summary>flat · perKm · perHour · percent. See Rules.QuoteBasis.</summary>
    public string Basis { get; set; } = "flat";

    /// <summary>Baht, or a percentage when the basis says so.</summary>
    public decimal Rate { get; set; }

    /// <summary>Offered on the quotation screen. Retired options stay for the history.</summary>
    public bool Active { get; set; } = true;

    public int Position { get; set; }
}

/// <summary>
/// The one number that belongs to the card rather than to any row of it.
///
/// A single row. It could have been a column repeated down the vehicle table,
/// and then the margin would have existed eleven times and drifted the first
/// time somebody edited ten of them.
///
/// Which row it is, is settled by <c>QuoteCardService.MarginRowAsync</c> and not
/// by its id: the id is an identity column, so the number belongs to the
/// database. Code that went looking for id 1 found nothing on an environment
/// where no margin had been set yet.
/// </summary>
public class QuoteSetting
{
    public int Id { get; set; }

    /// <summary>The margin quoted at, as a percentage of cost.</summary>
    public decimal MarginPercent { get; set; } = 10m;

    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
