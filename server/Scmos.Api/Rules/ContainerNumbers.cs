namespace Scmos.Api.Rules;

/// <summary>
/// What a container number is, and whether one is genuine.
///
/// <para>
/// ISO 6346: an owner code of three letters, a category letter (U for a
/// freight container, J for detachable equipment, Z for a chassis), six
/// digits, and a check digit. The check digit is computed from the other ten
/// — each letter and digit is given a value, weighted by a power of two by
/// position, summed, taken modulo eleven, and a result of ten becomes zero.
/// A number read off a photograph with one digit wrong fails it; the seven
/// real containers checked from the register pass it.
/// </para>
///
/// <para>
/// The check decides only where a number was <i>read</i> rather than typed
/// by a person looking at a booking: a model's reading of a photo. A
/// haulier's typed number that fails is still looked up — the register may
/// carry the same typo, keyed from the same document — and the parser only
/// notes it.
/// </para>
/// </summary>
public static class ContainerNumbers
{
    /// <summary>
    /// The value of each letter, A to Z. A is 10 and the count runs on,
    /// skipping every multiple of eleven — so K is 21, L is 23, U is 32 and Z
    /// is 38 — which is what makes the digit sensitive to a letter read as its
    /// neighbour.
    /// </summary>
    private static readonly int[] LetterValues = Build();

    private static int[] Build()
    {
        var values = new int[26];
        var next = 10;
        for (var i = 0; i < 26; i++)
        {
            if (next % 11 == 0) next++;
            values[i] = next++;
        }
        return values;
    }

    /// <summary>Upper case, letters and digits only — "temu 759276-5" is "TEMU7592765".</summary>
    public static string Normalise(string? text)
    {
        var built = new System.Text.StringBuilder();
        foreach (var c in text ?? "")
        {
            if (char.IsAsciiLetterOrDigit(c)) built.Append(char.ToUpperInvariant(c));
        }
        return built.ToString();
    }

    /// <summary>Whether this has the shape: four letters, the fourth U, J or Z, then seven digits.</summary>
    public static bool IsShaped(string? number)
    {
        var text = number ?? "";
        if (text.Length != 11) return false;
        for (var i = 0; i < 4; i++) if (!char.IsAsciiLetterUpper(text[i])) return false;
        if (text[3] is not ('U' or 'J' or 'Z')) return false;
        for (var i = 4; i < 11; i++) if (!char.IsAsciiDigit(text[i])) return false;
        return true;
    }

    /// <summary>
    /// Whether this is a number short of its check digit: four letters, the
    /// fourth U, J or Z, then six digits. On a box door the check digit sits
    /// in its own small frame after the serial — "GCXU 513490 [0]" — and a
    /// reading that stops at the frame is this shape. Not a number; a
    /// fragment that the register or a typed message may complete.
    /// </summary>
    public static bool IsFragment(string? number)
    {
        var text = number ?? "";
        if (text.Length != 10) return false;
        for (var i = 0; i < 4; i++) if (!char.IsAsciiLetterUpper(text[i])) return false;
        if (text[3] is not ('U' or 'J' or 'Z')) return false;
        for (var i = 4; i < 10; i++) if (!char.IsAsciiDigit(text[i])) return false;
        return true;
    }

    /// <summary>
    /// The full number inside some text that carries <paramref name="fragment"/>
    /// — a register cell, a typed message — or null. Letters and digits only
    /// are kept, then every eleven-character window that is shaped like a
    /// number is tried; "GCXU 513490-0 ถึงโรงงาน 05:00" gives GCXU5134900.
    /// </summary>
    public static string? Find(string? text, string fragment)
    {
        if (fragment.Length == 0) return null;
        var flat = Normalise(text);
        for (var i = 0; i + 11 <= flat.Length; i++)
        {
            var window = flat.Substring(i, 11);
            if (IsShaped(window) && window.Contains(fragment, StringComparison.Ordinal)) return window;
        }
        return null;
    }

    /// <summary>The check digit the first ten characters call for, or -1 when they are not a number.</summary>
    public static int CheckDigitOf(string? number)
    {
        var text = number ?? "";
        if (text.Length < 10) return -1;
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var c = text[i];
            int value;
            if (char.IsAsciiDigit(c)) value = c - '0';
            else if (char.IsAsciiLetterUpper(c)) value = LetterValues[c - 'A'];
            else return -1;
            sum += value << i;
        }
        return sum % 11 % 10;
    }

    /// <summary>Whether the number is shaped like one and its check digit agrees.</summary>
    public static bool IsValid(string? number) =>
        IsShaped(number) && CheckDigitOf(number) == number![10] - '0';
}
