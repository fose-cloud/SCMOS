using Scmos.Api.Rules;

namespace Scmos.Api.Data;

public static class CarrierOperationsCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-operations")) return null;
        var output = Console.Out;
        var failed = 0;
        void Check(bool condition, string message)
        {
            output.WriteLine($"  {(condition ? "ok  " : "FAIL")} {message}");
            if (!condition) failed++;
        }

        var forward = CarrierOperations.Decide("EXPORT", JobStatus.SupplierConfirmed, CarrierOperations.Dispatched);
        Check(forward.Decision == CarrierOperationDecision.Apply, "a confirmed job can be dispatched");
        Check(forward.TargetStatus == JobStatus.Dispatched, "dispatch maps to the existing job status");
        Check(forward.Stage == Stage.Dispatched, "dispatch maps to the existing milestone stage");

        var replay = CarrierOperations.Decide("EXPORT", JobStatus.Delivered, CarrierOperations.Delivered);
        Check(replay.Decision == CarrierOperationDecision.Replay, "the same operational event is idempotent");

        var backwards = CarrierOperations.Decide("EXPORT", JobStatus.InTransit, CarrierOperations.PickedUp);
        Check(backwards.Decision == CarrierOperationDecision.Refuse, "a carrier cannot move a job backwards");

        var completed = CarrierOperations.Decide("IMPORT", JobStatus.Completed, CarrierOperations.DeliveryComplete);
        Check(completed.Decision == CarrierOperationDecision.Replay, "Delivery Complete is idempotent");

        var closed = CarrierOperations.Decide("IMPORT", JobStatus.Completed, CarrierOperations.Delivered);
        Check(closed.Decision == CarrierOperationDecision.Refuse, "a completed job cannot be reopened");

        var cancelled = CarrierOperations.Decide("IMPORT", JobStatus.Cancelled, CarrierOperations.Delivered);
        Check(cancelled.Decision == CarrierOperationDecision.Refuse, "a cancelled job cannot advance");

        var invalidForCategory = CarrierOperations.Decide("DELIVERY", JobStatus.PickedUp, CarrierOperations.Loading);
        Check(invalidForCategory.Decision == CarrierOperationDecision.Refuse,
            "a status outside the category ladder is refused");

        var finish = CarrierOperations.Decide("DELIVERY", JobStatus.Delivered, CarrierOperations.DeliveryComplete);
        Check(finish.Decision == CarrierOperationDecision.Apply, "a delivered job can become Delivery Complete");
        Check(finish.TargetStatus == JobStatus.Completed, "Delivery Complete reuses COMPLETED");
        Check(finish.Stage == Stage.Closed, "Delivery Complete records the existing Closed milestone");

        var unknown = CarrierOperations.Decide("IMPORT", JobStatus.InTransit, "teleported");
        Check(unknown.Decision == CarrierOperationDecision.Refuse, "an unknown status is refused");

        output.WriteLine(failed == 0
            ? "Carrier Operations Phase 3 checks passed."
            : $"Carrier Operations Phase 3 checks failed: {failed}");
        return failed == 0 ? 0 : 1;
    }
}
