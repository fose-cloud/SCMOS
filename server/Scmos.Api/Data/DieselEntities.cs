namespace Scmos.Api.Data;

/// <summary>
/// A published diesel price, and the day it took effect.
///
/// <para>
/// One row per <b>change</b>, not per day. That is how PTT OR publish it — July
/// 2569 had four changes, on the 3rd, the 8th, the 22nd and the 23rd — and it is
/// thirty-one times less to key in. The daily prices and the month's average are
/// worked out from these; see dieselMonth.ts, which is checked against the
/// account team's own May'25 sheet and reproduces its 36.05.
/// </para>
///
/// <para>
/// A price is kept after it has been superseded. The average of a month that has
/// closed has to stay reproducible: an invoice queried in November is queried
/// against the figure August was billed at, not against whatever the table would
/// compute today.
/// </para>
/// </summary>
public class DieselPrice
{
    public long Id { get; set; }

    /// <summary>dd/MM/yyyy — the day this price took effect. Unique.</summary>
    public string EffectiveDate { get; set; } = "";

    /// <summary>Baht per litre, as published. Two places.</summary>
    public decimal Price { get; set; }

    /// <summary>Where it was read from — the source page, or who said so.</summary>
    public string Source { get; set; } = "";

    public string RecordedBy { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}
