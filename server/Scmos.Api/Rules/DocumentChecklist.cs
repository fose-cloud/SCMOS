namespace Scmos.Api.Rules;

/// <param name="Folder">Where it goes, from <see cref="BlobPaths.JobFolders"/>.</param>
/// <param name="Why">What goes wrong without it. Written down so the list can be argued with.</param>
public record RequiredDocument(string Folder, string English, string Thai, bool Blocking, string Why);

/// <summary>
/// What paperwork a job needs, and which of it stops the job.
///
/// The list differs by category because the work does. An import collects a
/// delivery order and a gate pass and produces a POD; a domestic delivery has no
/// container to gate in with and no E-Card to mismatch. Asking a delivery job
/// for an E-Card would train people to ignore the checklist, which is the only
/// way a checklist actually fails.
///
/// <c>Blocking</c> is the honest half: two of these genuinely stop a truck at
/// the gate, and the rest are things somebody chases afterwards. Marking all of
/// them blocking would be tidier and false.
/// </summary>
public static class DocumentChecklist
{
    private static readonly RequiredDocument Booking = new(
        "Booking", "Booking / DO", "ใบจองหรือ Delivery Order", true,
        "ผู้ขนส่งไม่รู้ว่าไปรับอะไรที่ไหน");

    private static readonly RequiredDocument ECard = new(
        "ECard", "E-Card / Gate Pass", "E-Card หรือบัตรผ่านประตู", true,
        "รถเข้าท่าไม่ได้ ถ้าเลขตู้บนบัตรไม่ตรงกับ booking");

    private static readonly RequiredDocument Pod = new(
        "POD", "Proof of Delivery", "ใบรับของ", false,
        "วางบิลไม่ได้ และไม่มีหลักฐานว่าส่งถึงจริง");

    private static readonly RequiredDocument Photos = new(
        "Images", "Photos", "รูปหน้างาน", false,
        "ถ้ามีข้อพิพาทเรื่องความเสียหาย จะไม่มีอะไรยืนยัน");

    private static readonly RequiredDocument Invoice = new(
        "Invoice", "Supplier Invoice", "ใบแจ้งหนี้ผู้ขนส่ง", false,
        "ต้องได้รับภายใน 4 วันหลังงานเสร็จตาม KPI การวางบิล");

    public static IReadOnlyList<RequiredDocument> For(string category) =>
        (category ?? "").Trim().ToUpperInvariant() switch
        {
            "DELIVERY" => [Booking, Pod, Photos, Invoice],
            _ => [Booking, ECard, Pod, Photos, Invoice],
        };

    /// <summary>
    /// Whether this document is expected yet.
    ///
    /// A POD before the truck has left is not missing, it is early — and an
    /// alert for it teaches people that the checklist cries wolf. Booking and
    /// E-Card are wanted from the start; the rest only once the job has run.
    /// </summary>
    public static bool ExpectedNow(RequiredDocument document, string status) =>
        document.Folder is "Booking" or "ECard"
        || JobRules.IsDone(status)
        || JobStatus.IsRunning(JobStatus.FromLegacy(status));
}
