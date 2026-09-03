using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the journey-matching rule against the spellings the register really
/// holds, with `--check-journey`.
///
/// Both ways of being wrong cost something real. Too strict and a distance
/// typed against "BKK port" is invisible to somebody typing "BKK Port", so it
/// gets entered again with a different number and the same road ends up quoted
/// two ways. Too loose and "PAT" matches "Pattaya" — two hundred kilometres
/// apart — and a quotation goes out priced for the wrong journey.
///
/// The spellings below are taken from the rate workbook, including the double
/// space in "BKK  port", which is in the file.
/// </summary>
public static class JourneyCheck
{
    private static readonly (string Why, string A, string B, bool Same)[] Places =
    [
        ("the same words in a different case", "BKK Port", "bkk port", true),
        ("a second space is not a second place", "BKK  port", "BKK port", true),
        ("a shorter name for the same place", "LCB", "LCB Port", true),
        ("and the other way round", "LCB Port", "LCB", true),
        // Dotted names are real — "A.N.I Logistics", "P.S.P. Specialties",
        // "Rojana I.E." are all in the register — but every one of them is a
        // company inside a long address, never a port. So the dots are left as
        // separators and the subset rule carries the case that matters.
        ("a name with initials still matches the plain one", "Rojana I.E.", "Rojana", true),
        ("punctuation does not join what it separates", "P.S.P. Specialties", "PSP", false),
        ("Thai survives being flattened", " แหลมฉบัง ", "แหลมฉบัง", true),

        ("two different ports are two places", "BKK port", "BMT port", false),
        ("one word inside another is not a match", "PAT", "Pattaya", false),
        ("nor is it the other way round", "Pattaya", "PAT", false),
        ("different Thai places stay different", "แหลมฉบัง", "ชลบุรี", false),
        ("nothing matches nothing", "", "LCB", false),
        ("and an empty name matches nothing at all", "", "", false),
    ];

    private static readonly (string Why, string[] Journey, string[] Other, bool Same)[] Journeys =
    [
        ("both ends the same is the same journey",
            ["LCB Port", "Amata"], ["lcb port", "AMATA"], true),
        ("the same road the other way is not the same journey",
            ["LCB Port", "Amata"], ["Amata", "LCB Port"], false),
        ("one end different is a different journey",
            ["LCB Port", "Amata"], ["LCB Port", "Rayong"], false),
    ];

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-journey")) return null;

        var failed = 0;
        Console.WriteLine("When two written places are the same place.");
        Console.WriteLine();

        foreach (var (why, a, b, want) in Places)
        {
            var got = JourneyKey.SamePlace(a, b);
            var ok = got == want;
            if (!ok) failed++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {Show(a),-14} {(got ? "==" : "!=")} {Show(b),-14} ({why})");
        }

        Console.WriteLine();
        foreach (var (why, one, other, want) in Journeys)
        {
            var got = JourneyKey.SameJourney(one[0], one[1], other[0], other[1]);
            var ok = got == want;
            if (!ok) failed++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {one[0]}→{one[1],-12} {(got ? "==" : "!=")} "
                + $"{other[0]}→{other[1],-12} ({why})");
        }

        // The key is what the database indexes on, so two spellings of one
        // journey must produce one string and not merely compare equal.
        Console.WriteLine();
        var keyed = JourneyKey.Of("BKK  Port", "Amata");
        var again = JourneyKey.Of("bkk port", "AMATA");
        var keysAgree = keyed == again;
        if (!keysAgree) failed++;
        Console.WriteLine($"{(keysAgree ? "ok  " : "FAIL")}  one key for one journey: {keyed}");

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? $"{Places.Length + Journeys.Length + 1} cases, all as intended." : $"{failed} wrong.");
        return failed == 0 ? 0 : 1;
    }

    private static string Show(string value) => value.Length == 0 ? "(empty)" : value;
}
