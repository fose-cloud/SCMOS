namespace Scmos.Api.Data;

/*
 * A customer's own paperwork: the prices agreed with them, and the shape of the
 * receipt they sign for.
 *
 * Both live in their own tables rather than in the ones next door that look
 * like they would fit, and in both cases for the same reason.
 *
 * The rates are not in RateLane because that table is the subcontractor book:
 * eighteen carriers quoting the same lanes so a job can be given to the
 * cheapest of them. A price negotiated for one customer's distribution runs
 * would be read out of there and offered on somebody else's job. Keeping them
 * apart is not tidiness, it is the difference between quoting a rate we hold
 * and quoting a rate we do not.
 *
 * The bands are not in FuelBand for a smaller version of the same reason: that
 * band list is LESCHACO's fuel clause, shared across every carrier so two of
 * them quoting the same band land in the same column of the comparison. A
 * customer's card carries its own clause, and merging the two would silently
 * move every subcontractor's prices into bands they never quoted.
 */

/// <summary>One step of a customer's own fuel clause.</summary>
public class CustomerRateBand
{
    public int Id { get; set; }

    /// <summary>Whose card this belongs to, as the register spells them.</summary>
    public string Customer { get; set; } = "";

    public string Label { get; set; } = "";
    public decimal MinPrice { get; set; }
    public decimal MaxPrice { get; set; }

    /// <summary>Position in fuel order. Lane prices index against this.</summary>
    public int Position { get; set; }
}

/// <summary>One priced route on a customer's card.</summary>
public class CustomerRateLane
{
    public long Id { get; set; }
    public string Customer { get; set; } = "";

    /// <summary>Who runs it — the card quotes more than one hauler.</summary>
    public string Carrier { get; set; } = "";

    public string FromPlace { get; set; } = "";
    public string ToPlace { get; set; } = "";

    /// <summary>Destination postcode, which is how these routes are identified.</summary>
    public string PostalCode { get; set; } = "";
}

/// <summary>One price: a lane, a truck size, and which fuel band it holds at.</summary>
public class CustomerRatePrice
{
    public long Id { get; set; }
    public long LaneId { get; set; }
    public string Vehicle { get; set; } = "";
    public int BandPosition { get; set; }
    public int Price { get; set; }
}

/// <summary>
/// The cargo receipt as one customer asks for it.
///
/// The heading block of ISO-FRM-TH-CCL-04-01 is the same for everybody. The
/// item table is not: of the fifty-five copies the operators keep, thirty-seven
/// ask for a PO number and a package count, fifteen for a D-code and a quantity
/// in kilos, and the rest are one-offs. So the columns are stored per customer
/// and the form reads them, rather than one set being hard-coded and quietly
/// handing a third of these customers the wrong document.
/// </summary>
public class CargoFormTemplate
{
    public int Id { get; set; }

    /// <summary>Taken from the file name, which is the only reliable label on
    /// these files — the customer cell inside them holds whatever the last
    /// person to reuse the file happened to type.</summary>
    public string Customer { get; set; } = "";

    /// <summary>The workbook it was read from, so the source can be found again.</summary>
    public string SourceFile { get; set; } = "";

    /// <summary>The item-table headings, tab separated in the order they print.</summary>
    public string Columns { get; set; } = "";
}
