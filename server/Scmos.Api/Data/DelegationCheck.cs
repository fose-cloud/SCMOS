using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the delegation rules against a fixture, with `--check-delegation`.
///
/// This is a write-permission rule: it decides whose jobs a person may change.
/// Until now the only way to exercise it was a database and a signed-in
/// session, so in practice it was never exercised at all — and the two ways it
/// can be wrong are not symmetric. Too narrow and somebody cannot do the work
/// they were asked to cover; too wide and somebody edits a colleague's
/// shipments with no arrangement behind it.
///
/// The awkward cases are the point. Both end dates are inclusive, so the first
/// and last day of a leave are checked by name. A revoked grant, a grant made
/// out to somebody else, and a grant whose dates will not parse must all come
/// back empty rather than being treated as absent-and-therefore-harmless.
/// </summary>
public static class DelegationCheck
{
    private static JobDelegation Grant(string owner, string to, string from, string until,
        bool revoked = false) =>
        new()
        {
            OwnerId = owner, DelegateId = to,
            FromDate = from, ToDate = until,
            Reason = "ลาพักร้อน", Revoked = revoked,
        };

    private static readonly DateOnly Today = new(2026, 9, 10);

    private static readonly (string Why, JobDelegation[] Grants, string Who, string[] Expect)[] Cases =
    [
        ("a live grant is the whole point",
            [Grant("OP-01", "OP-02", "05/09/2026", "15/09/2026")], "OP-02", ["OP-01"]),

        ("the first day of the leave counts — it is inclusive",
            [Grant("OP-01", "OP-02", "10/09/2026", "15/09/2026")], "OP-02", ["OP-01"]),

        ("and so does the last, which is the day people actually get wrong",
            [Grant("OP-01", "OP-02", "01/09/2026", "10/09/2026")], "OP-02", ["OP-01"]),

        ("the day before it starts grants nothing",
            [Grant("OP-01", "OP-02", "11/09/2026", "15/09/2026")], "OP-02", []),

        ("the day after it ends grants nothing — expiry needs nothing to run",
            [Grant("OP-01", "OP-02", "01/09/2026", "09/09/2026")], "OP-02", []),

        ("a revoked grant is over, whatever its dates say",
            [Grant("OP-01", "OP-02", "05/09/2026", "15/09/2026", revoked: true)], "OP-02", []),

        ("somebody else's grant is not yours to use",
            [Grant("OP-01", "OP-03", "05/09/2026", "15/09/2026")], "OP-02", []),

        ("two grants for the same owner answer once",
            [Grant("OP-01", "OP-02", "05/09/2026", "12/09/2026"),
             Grant("OP-01", "OP-02", "08/09/2026", "20/09/2026")], "OP-02", ["OP-01"]),

        ("two owners, both live, both answered",
            [Grant("OP-01", "OP-02", "05/09/2026", "15/09/2026"),
             Grant("OP-04", "OP-02", "01/09/2026", "30/09/2026")], "OP-02", ["OP-01", "OP-04"]),

        ("live and expired together — only the live one",
            [Grant("OP-01", "OP-02", "05/09/2026", "15/09/2026"),
             Grant("OP-04", "OP-02", "01/08/2026", "31/08/2026")], "OP-02", ["OP-01"]),

        // Dates that will not parse must not read as "no limit". A grant whose
        // window cannot be established is a grant that cannot be relied on.
        ("an unreadable start date grants nothing",
            [Grant("OP-01", "OP-02", "", "15/09/2026")], "OP-02", []),
        ("an unreadable end date grants nothing",
            [Grant("OP-01", "OP-02", "05/09/2026", "soon")], "OP-02", []),

        // The comment on ActingFor has always claimed this; now it is true.
        ("a grant to yourself adds nothing — you already have your own work",
            [Grant("OP-02", "OP-02", "05/09/2026", "15/09/2026")], "OP-02", []),

        ("nobody signed in acts for nobody",
            [Grant("OP-01", "OP-02", "05/09/2026", "15/09/2026")], "", []),

        ("no grants at all is the ordinary answer for almost everybody",
            [], "OP-02", []),
    ];

    private static readonly (string Why, JobDelegation Grant, string Expect)[] Descriptions =
    [
        ("revoked reads as revoked before anything else",
            Grant("OP-01", "OP-02", "05/09/2026", "15/09/2026", revoked: true), "ยกเลิกแล้ว"),
        ("a live grant says so", Grant("OP-01", "OP-02", "05/09/2026", "15/09/2026"), "กำลังใช้งาน"),
        ("a future grant is waiting, not expired",
            Grant("OP-01", "OP-02", "20/09/2026", "25/09/2026"), "รอถึงกำหนด"),
        ("a past grant has expired", Grant("OP-01", "OP-02", "01/08/2026", "31/08/2026"), "หมดอายุแล้ว"),
    ];

    private static StaffMember Person(string id, string role, bool active = true) =>
        new() { Id = id, Name = id, Role = role, Active = active };

    /// <summary>
    /// Who may be handed somebody else's work. The list the form offers and the
    /// rule the grant validates with are the same reading, so a name that
    /// appears in the dropdown cannot be refused on the way in.
    /// </summary>
    private static readonly (string Why, StaffMember Person, string Owner, bool Expect)[] Receivers =
    [
        ("a colleague on the operations team can cover", Person("OP-03", "Operation User"), "OP-01", true),
        ("so can a supervisor", Person("OP-05", "Operation Supervisor"), "OP-01", true),
        ("and an assistant manager", Person("AM-01", "Assistant Manager"), "OP-01", true),
        ("and a manager", Person("MG-02", "Manager"), "OP-01", true),

        // Named, not excluded: these are refused because they are not on the
        // list, which is also what happens to any role added later.
        ("an administrator has the rights but not the work",
            Person("AD-01", "Administrator"), "OP-01", false),
        ("management has the rights but not the work", Person("MG-01", "Management"), "OP-01", false),
        ("customer service does not run the register", Person("CS-01", "CS"), "OP-01", false),
        ("a viewer least of all", Person("VW-01", "Viewer"), "OP-01", false),
        ("a carrier's account works its own jobs, not the register's",
            Person("SUB-01", "Subcontractor"), "OP-01", false),
        ("a role nobody has decided about yet is refused, not allowed",
            Person("NEW-01", "Some Future Role"), "OP-01", false),

        ("a closed account cannot be given work", Person("OP-03", "Operation User", active: false), "OP-01", false),
        ("nobody covers for themselves", Person("OP-01", "Operation User"), "OP-01", false),
    ];

    private static DateOnly Day(int day, int month = 9, int year = 2026) => new(year, month, day);

    /// <summary>
    /// The three limits that keep a grant a holiday arrangement rather than a
    /// quiet permanent change of who owns the work. Today is 10/09/2026.
    /// </summary>
    private static readonly (string Why, DateOnly From, DateOnly To, JobDelegation[] Existing, bool Allowed)[] Limits =
    [
        ("an ordinary week off is fine", Day(14), Day(18), [], true),
        ("starting today is fine — that is the day somebody arranges cover",
            Day(10), Day(12), [], true),

        ("starting yesterday is not — a backdated grant reads as an alibi",
            Day(9), Day(12), [], false),
        ("nor is a start last month", Day(1, 8), Day(30, 9), [], false),

        ("ninety days exactly is allowed", Day(10), Day(8, 12), [], true),
        ("ninety-one is not — that is a change of owner, not a leave",
            Day(10), Day(9, 12), [], false),
        ("and neither is a grant running to 2099",
            Day(10), Day(31, 12, 2099), [], false),

        ("the end cannot precede the start", Day(18), Day(14), [], false),

        ("a second grant to the same person over the same days is one entered twice",
            Day(14), Day(18), [Grant("OP-01", "OP-02", "12/09/2026", "16/09/2026")], false),
        ("touching at one end still overlaps",
            Day(14), Day(18), [Grant("OP-01", "OP-02", "10/09/2026", "14/09/2026")], false),
        ("a day apart does not overlap",
            Day(16), Day(18), [Grant("OP-01", "OP-02", "10/09/2026", "14/09/2026")], true),
        ("a revoked grant is not in the way",
            Day(14), Day(18), [Grant("OP-01", "OP-02", "12/09/2026", "16/09/2026", revoked: true)], true),
        ("somebody else covering the same days is a real arrangement, not a clash",
            Day(14), Day(18), [Grant("OP-01", "OP-03", "12/09/2026", "16/09/2026")], true),
    ];

    /// <summary>
    /// Null when this is not the flag being asked for; otherwise the exit code,
    /// so a failing check can stop a build rather than only saying so.
    /// </summary>
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-delegation")) return null;

        var failed = 0;
        Console.WriteLine($"Today is {Today:dd/MM/yyyy} for every case below.");
        Console.WriteLine();

        foreach (var (why, grants, who, expect) in Cases)
        {
            var got = DelegationService.ActingFor(grants, who, Today).OrderBy(id => id).ToArray();
            var want = expect.OrderBy(id => id).ToArray();
            var ok = got.SequenceEqual(want, StringComparer.Ordinal);
            if (!ok) failed++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {Show(who),-6} -> {Show(got),-18} "
                + (ok ? "" : $"expected {Show(want)}  ") + $"({why})");
        }

        Console.WriteLine();
        foreach (var (why, grant, expect) in Descriptions)
        {
            var got = DelegationService.Describe(grant, Today);
            var ok = got == expect;
            if (!ok) failed++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  status -> {got,-14} "
                + (ok ? "" : $"expected {expect}  ") + $"({why})");
        }

        Console.WriteLine();
        foreach (var (why, person, owner, expect) in Receivers)
        {
            var got = DelegationService.CanReceive(person, owner);
            var ok = got == expect;
            if (!ok) failed++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {person.Id} ({person.Role}) -> "
                + $"{(got ? "may cover" : "may not"),-10} " + (ok ? "" : "wrong  ") + $"({why})");
        }

        Console.WriteLine();
        foreach (var (why, from, to, existing, allowed) in Limits)
        {
            var refused = DelegationService.WhyRefused(from, to, Today, "OP-02", existing);
            var ok = (refused is null) == allowed;
            if (!ok) failed++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {from:dd/MM}–{to:dd/MM/yyyy} -> "
                + $"{(refused is null ? "allowed" : "refused"),-8} " + (ok ? "" : "wrong  ") + $"({why})");
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"{Cases.Length} permission cases, {Receivers.Length} candidate rules, "
              + $"{Limits.Length} grant limits and {Descriptions.Length} statuses, all as expected."
            : $"{failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static string Show(string who) => who.Length == 0 ? "(none)" : who;
    private static string Show(string[] ids) => ids.Length == 0 ? "(nothing)" : string.Join(",", ids);
}
