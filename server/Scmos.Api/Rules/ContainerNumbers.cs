namespace Scmos.Api.Rules;

/// <summary>
/// What a container number is shaped like.
///
/// <para>
/// ISO 6346: an owner code of three letters, a category letter (U for a
/// freight container, J for detachable equipment, Z for a chassis), and
/// seven digits. The seventh is a check digit computed from the other ten;
/// that arithmetic lived here while a driver's photo was read for its
/// number (16–17 Sep 2026) and left with it — a number a haulier types is
/// looked up as typed, since the register may carry the same slip, keyed
/// from the same document.
/// </para>
/// </summary>
public static class ContainerNumbers
{
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
}
