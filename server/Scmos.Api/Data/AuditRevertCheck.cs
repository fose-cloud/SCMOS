using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// <c>--check-audit-revert</c>: putting a job's cell back from the trail is
/// a new edit with the grid's own authority, on the cells the trail keeps,
/// only while the cell still holds what the row says it became.
/// </summary>
public static class AuditRevertCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-audit-revert")) return null;

        var failed = 0;
        Console.WriteLine("Reverting from the trail: a new edit, the grid's own authority, only while the cell still holds the row's word.");
        Console.WriteLine();

        /* ---- the way back from a label to a cell ---- */
        failed += Say("every significant field's label leads back to it",
            new[] { "trucker", "status", "licence", "driver", "op", "date", "planTime", "container" }
                .All(field => AuditActions.For(field) is { } found && AuditActions.FieldOf(found.Label) == field), true);
        failed += Say("a label that is not ours leads nowhere", AuditActions.FieldOf("หมายเหตุ") is null && AuditActions.FieldOf("") is null, true);
        failed += Say("the label is matched trimmed, exactly", AuditActions.FieldOf(" ผู้รับผิดชอบ ") == "op" && AuditActions.FieldOf("ผู้รับผิดชอบงาน") is null, true);

        /* ---- what may come back ---- */
        failed += Say("an assignment, an update, a status and a carrier change on a job may come back",
            AuditRevert.Reversible("job", "assign", "ผู้รับผิดชอบ") && AuditRevert.Reversible("job", "update", "ทะเบียนรถ")
            && AuditRevert.Reversible("job", "status", "สถานะ") && AuditRevert.Reversible("job", "carrier", "ผู้ขนส่ง"), true);
        failed += Say("a supplier's or a rate's row does not", AuditRevert.Problem("supplier", "update", "ผู้รับผิดชอบ") == "not-a-job"
            && AuditRevert.Problem("rate", "rate", "ราคา") == "not-a-job", true);
        failed += Say("an approval, a delete, an upload or a bulk replace is not a cell change",
            AuditRevert.Problem("job", "approve", "ผู้รับผิดชอบ") == "not-a-cell-change" && AuditRevert.Problem("job", "delete", "") == "not-a-cell-change"
            && AuditRevert.Problem("job", "bulk-replace", "") == "not-a-cell-change", true);
        failed += Say("a row about a cell the trail does not name cannot come back", AuditRevert.Problem("job", "update", "หมายเหตุ") == "unknown-cell", true);

        /* ---- whose authority ---- */
        failed += Say("assigning back needs AssignJobs; any other cell needs EditAnyJob",
            AuditRevert.Required("op") == Capability.AssignJobs && AuditRevert.Required("trucker") == Capability.EditAnyJob
            && AuditRevert.Required("status") == Capability.EditAnyJob, true);
        failed += Say("a supervisor holds both; an operator neither",
            Roles.Can(Roles.Supervisor, AuditRevert.Required("op")) && Roles.Can(Roles.Supervisor, AuditRevert.Required("status"))
            && !Roles.Can(Roles.Operation, AuditRevert.Required("op")) && !Roles.Can(Roles.Operation, AuditRevert.Required("status")), true);

        /* ---- only while the cell still holds the row's word ---- */
        failed += Say("a name is the same name however it was typed", AuditRevert.StillHolds("op", " uthai", "Uthai") && AuditRevert.StillHolds("trucker", "SANGJA ", "sangja"), true);
        failed += Say("a status or a plate is compared exactly", !AuditRevert.StillHolds("status", "delivered", "DELIVERED") && AuditRevert.StillHolds("licence", "70-1234 ", "70-1234"), true);
        failed += Say("a cell somebody changed since does not hold", !AuditRevert.StillHolds("op", "Watsana", "Uthai") && !AuditRevert.StillHolds("op", "", "Uthai"), true);

        /* ---- the row the revert leaves, and the order ---- */
        failed += Say("the revert's reason names the row it reverses", AuditRevert.ReasonFor(4712, "  มอบหมายผิดคน ") == "มอบหมายผิดคน — ย้อนกลับรายการ #4712", true);
        failed += Say("rows go back newest first", AuditRevert.InOrder(new long[] { 3, 9, 5 }, id => id).SequenceEqual([9L, 5L, 3L]), true);
        failed += Say("the batch is bounded and a reason is required", AuditRevert.MaxRows == 200 && AuditRevert.MinReason == 4, true);

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "All audit revert checks passed." : $"{failed} audit revert check(s) FAILED.");
        return failed == 0 ? 0 : 1;
    }

    private static int Say<T>(string why, T got, T want)
    {
        var ok = Equals(got, want);
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}{(ok ? "" : $"  (got {got}, wanted {want})")}");
        return ok ? 0 : 1;
    }
}
