using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Checks that no two capabilities share a bit, with <c>--check-capability</c>.
///
/// This exists because it happened. A capability added for the staff directory
/// was given <c>1 &lt;&lt; 14</c>, which <c>ManageTraining</c> already held, and
/// every account with training rights silently gained the user register with
/// it. Nothing failed to compile, nothing threw, and the only visible symptom
/// was <c>/api/me</c> listing "ManageTraining" twice.
///
/// A permission that arrives by accident is the worst kind of bug this codebase
/// can have, and the arithmetic that produces it is trivial to check.
/// </summary>
public static class CapabilityCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-capability")) return null;

        var values = Enum.GetValues<Capability>()
            .Where(one => one != 0)
            .Select(one => (Name: one.ToString(), Value: (long)one))
            .ToList();

        var failed = 0;

        Console.WriteLine("Every capability holds a bit of its own.");
        Console.WriteLine();

        foreach (var group in values.GroupBy(one => one.Value).OrderBy(one => one.Key))
        {
            var names = group.Select(one => one.Name).Distinct().ToList();
            var bit = (int)Math.Log2(group.Key);
            // Enum.GetValues collapses names that share a value, so a collision
            // shows up as a name that is not the one asked for rather than as
            // two entries. Both are caught below.
            var ok = names.Count == 1 && (group.Key & (group.Key - 1)) == 0;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  bit {bit,2}  {string.Join(" / ", names)}");
        }

        // The reliable test: ask each name for its own value back. Two names on
        // one bit cannot both round-trip.
        Console.WriteLine();
        Console.WriteLine("Each name round-trips to its own value.");
        Console.WriteLine();
        var names_ = Enum.GetNames<Capability>();
        var byValue = new Dictionary<long, string>();
        foreach (var name in names_)
        {
            var value = (long)Enum.Parse<Capability>(name);
            if (value == 0) continue;
            if (byValue.TryGetValue(value, out var already))
            {
                failed++;
                Console.WriteLine($"  FAIL  {name} shares bit {(int)Math.Log2(value)} with {already}");
                Console.WriteLine("        Anybody holding one silently holds the other.");
                continue;
            }
            byValue[value] = name;
            if ((value & (value - 1)) != 0)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name} = {value} is not a single bit");
            }
        }
        Console.WriteLine($"  {names_.Length - 1} capabilities, {byValue.Count} distinct bits");

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "No capability is granted by accident."
            : $"{failed} problem(s) — a permission is being granted by accident.");
        return failed == 0 ? 0 : 1;
    }
}
