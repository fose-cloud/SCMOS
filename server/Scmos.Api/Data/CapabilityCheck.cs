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
        failed += Holds(Roles.Operation, Capability.EditSuppliers, true);
        failed += Holds(Roles.Operation, Capability.ManageSuppliers, false);
        failed += Holds(Roles.Supervisor, Capability.EditSuppliers, true);
        failed += Holds(Roles.Supervisor, Capability.ManageSuppliers, true);
        failed += Holds(Roles.Subcontractor, Capability.EditSuppliers, false);
        failed += Holds(Roles.Viewer, Capability.EditSuppliers, false);
        failed += Holds(Roles.CustomerService, Capability.EditSuppliers, false);

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

        /*
         * Where the rate book's two doors are, now that they are two.
         *
         * Recording a quotation moved down to Operation User on 5 September
         * 2026; changing an agreed rate did not. The whole point of splitting
         * them is that the second did not follow the first, and the arithmetic
         * that would let it — one more flag in OperationGrants — is a one-word
         * edit nothing else would notice.
         */
        Console.WriteLine();
        Console.WriteLine("Recording a quotation and changing a rate are different doors.");
        Console.WriteLine();
        failed += Holds(Roles.Operation, Capability.QuoteToSheet, true);
        failed += Holds(Roles.Operation, Capability.EditRates, false);
        failed += Holds(Roles.Supervisor, Capability.QuoteToSheet, true);
        failed += Holds(Roles.Supervisor, Capability.EditRates, false);
        failed += Holds(Roles.Manager, Capability.QuoteToSheet, true);
        failed += Holds(Roles.Manager, Capability.EditRates, true);
        // A carrier holding either would be putting a price into a sheet that
        // seventeen of their competitors are read from.
        failed += Holds(Roles.Subcontractor, Capability.QuoteToSheet, false);
        failed += Holds(Roles.Subcontractor, Capability.ViewRates, false);
        failed += Holds(Roles.CustomerService, Capability.QuoteToSheet, false);
        failed += Holds(Roles.Viewer, Capability.QuoteToSheet, false);
        // An unrecognised role must not pick up the new flag either.
        failed += Holds("Operation Users", Capability.QuoteToSheet, false);

        /*
         * Reading a shared mailbox is an Administrator act.
         *
         * It went to Administrator alone rather than following ManagerGrants,
         * because the mailbox connection test returns the subject and sender of
         * a real message. Every role below reads company mail the moment this
         * flag drifts down to them, and it would drift the way every other one
         * nearly did: by being added to a grant that already contained the
         * thing next to it.
         */
        Console.WriteLine();
        Console.WriteLine("Connecting a mailbox is the Administrator's alone.");
        Console.WriteLine();
        failed += Holds(Roles.Admin, Capability.AdministerMailbox, true);
        failed += Holds(Roles.Manager, Capability.AdministerMailbox, false);
        failed += Holds(Roles.Supervisor, Capability.AdministerMailbox, false);
        failed += Holds(Roles.Operation, Capability.AdministerMailbox, false);
        // A carrier reading the shared mailbox would be reading every other
        // carrier's correspondence with us.
        failed += Holds(Roles.Subcontractor, Capability.AdministerMailbox, false);
        failed += Holds(Roles.CustomerService, Capability.AdministerMailbox, false);
        failed += Holds(Roles.Viewer, Capability.AdministerMailbox, false);
        failed += Holds("Administrators", Capability.AdministerMailbox, false);

        /*
         * Reading the shared mailbox is the operators' work; the carrier's
         * account must never be near it.
         *
         * One mailbox holds our correspondence with every haulier, so a
         * Subcontractor account with this flag would be reading seventeen
         * competitors' mail — the same reason that role cannot see the rate
         * book, and the same way it would arrive: by being added to a grant
         * that already contained the thing next to it.
         */
        Console.WriteLine();
        Console.WriteLine("The shared mailbox is the operators', and never a carrier's.");
        Console.WriteLine();
        failed += Holds(Roles.Operation, Capability.ViewMailbox, true);
        failed += Holds(Roles.Supervisor, Capability.ViewMailbox, true);
        failed += Holds(Roles.Manager, Capability.ViewMailbox, true);
        failed += Holds(Roles.Admin, Capability.ViewMailbox, true);
        failed += Holds(Roles.Subcontractor, Capability.ViewMailbox, false);
        failed += Holds(Roles.Viewer, Capability.ViewMailbox, false);
        // Reading is one permission; saying which job a message belongs to is
        // another, and Operation User does not hold the second.
        failed += Holds(Roles.Operation, Capability.EditAnyJob, false);
        failed += Holds(Roles.Supervisor, Capability.EditAnyJob, true);

        /*
         * The screen's list and this enum are the same list written twice.
         *
         * The map in Administration.tsx already carries a comment saying two
         * capabilities were once held by roles and shown by nothing, because
         * the permission matrix renders that map's keys — so a capability
         * missing from it is a permission the screen quietly denies having
         * granted. That was found by hand. This finds it in CI.
         */
        Console.WriteLine();
        Console.WriteLine("The permissions screen lists every capability there is.");
        Console.WriteLine();

        var screen = FindScreen();
        if (screen is null)
        {
            failed++;
            Console.WriteLine("  FAIL  could not find app/scmos/screens/Administration.tsx to check against");
        }
        else
        {
            var listed = File.ReadAllText(screen);
            foreach (var name in Enum.GetNames<Capability>())
            {
                if (name == nameof(Capability.None)) continue;
                // The key as the map writes it, so a name appearing only in a
                // comment or a call elsewhere in the file does not count.
                var ok = listed.Contains($"  {name}: \"", StringComparison.Ordinal);
                if (!ok) failed++;
                Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {name}");
                if (!ok) Console.WriteLine("        Held by a role, shown by nothing — add it to CAPABILITY_LABELS.");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "No capability is granted by accident."
            : $"{failed} problem(s) — a permission is being granted by accident.");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// The permissions screen, found by walking up from the binary until the
    /// repository root shows itself. Null when this is not running inside a
    /// checkout, which the caller reports rather than skips — a check that goes
    /// quiet when it cannot find its subject is not a check.
    /// </summary>
    private static string? FindScreen()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        for (var up = 0; up < 8 && here is not null; up++, here = here.Parent)
        {
            var candidate = Path.Combine(here.FullName, "app", "scmos", "screens", "Administration.tsx");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static int Holds(string role, Capability capability, bool want)
    {
        var got = Roles.Can(role, capability);
        var ok = got == want;
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {role,-22} {(want ? "holds    " : "does not hold")} {capability}");
        return ok ? 0 : 1;
    }
}
