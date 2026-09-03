using System.Text;

namespace Scmos.Api.Rules;

/// <summary>
/// When two people mean the same road.
///
/// Place names in this business are typed, never picked, so one port arrives as
/// "BKK port", "BKK  port" with two spaces, "BKK Port" and "bkk port" — four
/// spellings of one place, in the same column, often in the same month. A
/// distance stored against one of them cannot be found by any of the others, so
/// it gets typed again with a different number, and two people quote the same
/// journey differently.
///
/// <para>The key is the answer to "have we been here before". It lives on the
/// server and only here, because the browser needs the same answer and a rule
/// written on both sides of a wire is a rule that will disagree with itself the
/// first time one side is edited.</para>
/// </summary>
public static class JourneyKey
{
    /// <summary>
    /// A place as a list of its words, lowercased, punctuation gone.
    ///
    /// Thai is kept as it is: it does not case-fold and its letters are not
    /// punctuation, so แหลมฉบัง survives whole.
    /// </summary>
    public static string[] Words(string place)
    {
        var text = (place ?? "").ToLowerInvariant();
        var made = new StringBuilder(text.Length);
        foreach (var letter in text)
        {
            // Thai occupies U+0E00–U+0E7F; everything else is judged by category.
            var keep = char.IsLetterOrDigit(letter) || (letter >= '฀' && letter <= '๿');
            made.Append(keep ? letter : ' ');
        }
        return made.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>One journey's identity: both ends, flattened, in order.</summary>
    public static string Of(string from, string to) =>
        string.Join(" ", Words(from)) + "→" + string.Join(" ", Words(to));

    /// <summary>
    /// Whether two written places are the same one.
    ///
    /// Equal after flattening, or one names a subset of the other's words —
    /// "LCB" is the same place as "LCB Port", and somebody looking for a rate
    /// from one should find the other. Word by word rather than by substring,
    /// because "PAT" is inside "PATTAYA" and they are two hundred kilometres
    /// apart.
    /// </summary>
    public static bool SamePlace(string a, string b)
    {
        var left = Words(a);
        var right = Words(b);
        if (left.Length == 0 || right.Length == 0) return false;

        var (fewer, more) = left.Length <= right.Length ? (left, right) : (right, left);
        return fewer.All(word => more.Contains(word));
    }

    /// <summary>Whether both ends of two journeys are the same place.</summary>
    public static bool SameJourney(string fromA, string toA, string fromB, string toB) =>
        SamePlace(fromA, fromB) && SamePlace(toA, toB);
}
