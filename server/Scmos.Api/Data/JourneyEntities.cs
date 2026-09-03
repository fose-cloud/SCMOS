namespace Scmos.Api.Data;

/// <summary>
/// How far it is, once somebody has said so.
///
/// The calculator prices a journey from its distance, and the distance is typed
/// by a person — no map, no lookup, nothing leaving the building. Typed once,
/// though. Two of every five journeys in the register are quoted more than
/// once, and a number retyped from memory each time is a number that will
/// eventually differ from itself.
///
/// <para>Keyed on <see cref="Key"/> rather than on the words, because the words
/// arrive spelled four ways — see <see cref="Rules.JourneyKey"/>. The words are
/// kept as first written so the list reads the way a person wrote it.</para>
/// </summary>
public class JourneyDistance
{
    public int Id { get; set; }

    /// <summary>Both ends flattened, from Rules.JourneyKey.Of. Unique.</summary>
    public string Key { get; set; } = "";

    /// <summary>As the first person to record it wrote them.</summary>
    public string FromPlace { get; set; } = "";
    public string ToPlace { get; set; } = "";

    /// <summary>The outbound journey, in kilometres.</summary>
    public int Km { get; set; }

    /// <summary>Who said so, and when. A distance is a judgement, not a fact.</summary>
    public string SetBy { get; set; } = "";
    public DateTimeOffset SetAt { get; set; }

    /// <summary>How many times it has been used to price something.</summary>
    public int UsedCount { get; set; }
}
