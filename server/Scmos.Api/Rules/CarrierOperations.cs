namespace Scmos.Api.Rules;

public enum CarrierOperationDecision
{
    Apply,
    Replay,
    Refuse,
}

public sealed record CarrierStatusDecision(
    CarrierOperationDecision Decision, string TargetStatus, Stage Stage, string Message);

/// <summary>
/// The carrier-facing names for SCMOS's existing operational ladder.
/// This deliberately maps onto <see cref="JobStatus"/> and <see cref="Stage"/>
/// instead of creating a second status model for the portal.
/// </summary>
public static class CarrierOperations
{
    public const string Dispatched = "dispatched";
    public const string PickedUp = "picked_up";
    public const string Loading = "loading";
    public const string InTransit = "in_transit";
    public const string Delivered = "delivered";
    public const string ContainerReturned = "container_returned";
    public const string DeliveryComplete = "delivery_complete";

    public static readonly string[] Types =
        [Dispatched, PickedUp, Loading, InTransit, Delivered, ContainerReturned, DeliveryComplete];

    public static CarrierStatusDecision Decide(string category, string current, string? type)
    {
        var wanted = (type ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        var target = wanted switch
        {
            Dispatched => (JobStatus.Dispatched, Stage.Dispatched),
            PickedUp => (JobStatus.PickedUp, Stage.PickedUp),
            Loading => (JobStatus.Loading, Stage.Loading),
            InTransit => (JobStatus.InTransit, Stage.InTransit),
            Delivered => (JobStatus.Delivered, Stage.Delivered),
            ContainerReturned => (JobStatus.ContainerReturned, Stage.ContainerReturned),
            DeliveryComplete => (JobStatus.Completed, Stage.Closed),
            _ => ("", Stage.Received),
        };

        if (target.Item1.Length == 0)
            return new(CarrierOperationDecision.Refuse, "", target.Item2,
                "สถานะที่บันทึกได้: " + string.Join(", ", Types));

        var ladder = JobStatus.For(category);
        var targetAt = Array.FindIndex(ladder, value =>
            string.Equals(value, target.Item1, StringComparison.OrdinalIgnoreCase));
        if (targetAt < 0)
            return new(CarrierOperationDecision.Refuse, target.Item1, target.Item2,
                $"{target.Item1} ไม่อยู่ในขั้นตอนของงาน {category}");

        var now = Formats.Clean(current).ToUpperInvariant();
        if (now.Length == 0) now = JobStatus.Draft;
        if (string.Equals(now, target.Item1, StringComparison.OrdinalIgnoreCase))
            return new(CarrierOperationDecision.Replay, target.Item1, target.Item2, "บันทึกสถานะนี้ไว้แล้ว");
        if (now == JobStatus.Cancelled)
            return new(CarrierOperationDecision.Refuse, target.Item1, target.Item2, "งานถูกยกเลิกแล้ว");
        if (now == JobStatus.Completed)
            return new(CarrierOperationDecision.Refuse, target.Item1, target.Item2, "งานปิดแล้ว");

        var currentAt = Array.FindIndex(ladder, value =>
            string.Equals(value, now, StringComparison.OrdinalIgnoreCase));
        if (currentAt < 0) currentAt = Array.FindIndex(ladder, value =>
            string.Equals(value, JobStatus.FromLegacy(now), StringComparison.OrdinalIgnoreCase));
        if (currentAt >= targetAt)
            return new(CarrierOperationDecision.Refuse, target.Item1, target.Item2,
                $"ย้อนสถานะจาก {now} ไป {target.Item1} ไม่ได้");

        return new(CarrierOperationDecision.Apply, target.Item1, target.Item2, "บันทึกสถานะได้");
    }
}
