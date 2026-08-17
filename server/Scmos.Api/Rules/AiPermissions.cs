using Scmos.Api.Auth;
namespace Scmos.Api.Rules;

/// <summary>
/// What the assistant may do.
///
/// Three levels, and the difference between them is structural, not textual:
///
/// <c>Allow</c>  — the tool runs.
/// <c>Approval</c> — the tool returns a draft and an approval id. Nothing is
///                 written until a person approves that exact payload.
/// <c>Deny</c>   — the tool is not given to the agent at all.
///
/// Deny is the important one. A model cannot be instructed out of a capability
/// it does not have, and it can always be talked out of an instruction. There is
/// no delete tool in this catalogue and no code path that would call one, so
/// "AI must never delete records" is a property of the system rather than a
/// sentence in a prompt somebody may later edit.
/// </summary>
public enum AiPermission { Allow, Approval, Deny }

public record AiToolDefinition(string Name, string Agent, AiPermission Permission, string Description);

public static class AiPermissions
{
    public const string Operation = "operation";
    public const string Document = "document";
    public const string Kpi = "kpi";
    public const string SupplierAgent = "supplier";
    public const string Safety = "safety";
    public const string Management = "management";

    /// <summary>
    /// The catalogue, matching the agreed permission matrix.
    ///
    /// Reading anything is allowed. Drafting anything is allowed — a draft is
    /// words on a screen, not an action. Changing an operational record, a rate,
    /// sending an email or closing a CAR/PAR needs a person. Deleting is absent.
    /// </summary>
    public static readonly AiToolDefinition[] Catalogue =
    [
        // Read — the assistant is useless without these and they cost nothing.
        new("query_shipments", Operation, AiPermission.Allow, "อ่านงานขนส่งจากทะเบียน (อ่านอย่างเดียว)"),
        new("search_shipment", Operation, AiPermission.Allow, "ค้นหางานด้วยเลขตู้ / Job / ลูกค้า"),
        new("query_delays", Operation, AiPermission.Allow, "อ่านรายการความล่าช้าและสาเหตุ"),
        new("query_capacity", Operation, AiPermission.Allow, "อ่านกำลังรถที่ผู้ขนส่งแจ้งไว้"),
        new("read_document", Document, AiPermission.Allow, "อ่าน PDF / รูป / E-Card / POD จาก Blob"),
        new("analyze_excel", Document, AiPermission.Allow, "อ่านและวิเคราะห์ไฟล์ Excel"),
        new("calculate_kpi", Kpi, AiPermission.Allow, "คำนวณ KPI ทั้ง 8 ตัวตามช่วงเวลา"),
        new("analyze_kpi", Kpi, AiPermission.Allow, "วิเคราะห์แนวโน้มและเปรียบเทียบ KPI"),
        new("search_supplier", SupplierAgent, AiPermission.Allow, "ค้นหาและอ่านข้อมูลผู้ขนส่ง"),
        new("query_rates", SupplierAgent, AiPermission.Allow, "อ่านตารางราคาและคำนวณราคาตามราคาน้ำมัน"),
        new("recommend_supplier", SupplierAgent, AiPermission.Allow, "เสนอผู้ขนส่งที่เหมาะกับงาน (ข้อเสนอ ไม่ใช่การมอบหมาย)"),
        new("recommend_rate", SupplierAgent, AiPermission.Allow, "เสนอราคาที่ควรใช้ (ข้อเสนอ ไม่ใช่การเปลี่ยนราคา)"),
        new("query_incidents", Safety, AiPermission.Allow, "อ่านเหตุผิดปกติและ CAR/PAR"),
        new("generate_report", Management, AiPermission.Allow, "สร้างรายงานสรุปสำหรับผู้บริหาร"),

        // Draft — produces text for a person to use. Writes nothing.
        new("draft_carpar", Safety, AiPermission.Allow, "ร่างเนื้อหา CAR/PAR (ยังไม่บันทึก)"),
        new("draft_email", Management, AiPermission.Allow, "ร่างอีเมล (ยังไม่ส่ง)"),

        // Approval — returns a draft and an id; a person applies it.
        new("update_shipment", Operation, AiPermission.Approval, "แก้ไขข้อมูลงานขนส่ง — ต้องมีคนอนุมัติ"),
        new("update_rate", SupplierAgent, AiPermission.Approval, "เปลี่ยนราคา — ต้องมีคนอนุมัติ"),
        new("send_email", Management, AiPermission.Approval, "ส่งอีเมลจริง — ต้องมีคนอนุมัติ"),
        new("close_carpar", Safety, AiPermission.Approval, "ปิดเคส CAR/PAR — ต้องมีคนอนุมัติ"),
        new("assign_supplier", Operation, AiPermission.Approval, "มอบหมายผู้ขนส่งให้งาน — ต้องมีคนอนุมัติ"),
    ];

    /// <summary>
    /// Actions that are refused outright, kept by name so the refusal can be
    /// explained rather than looking like a missing feature. Nothing in
    /// <see cref="Catalogue"/> may share a name with these.
    /// </summary>
    public static readonly string[] Forbidden =
    [
        "delete_shipment", "delete_supplier", "delete_rate", "delete_document",
        "delete_incident", "purge_register", "drop_table",
    ];

    public static AiToolDefinition? Find(string name) =>
        Catalogue.FirstOrDefault(tool => tool.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static bool IsForbidden(string name) =>
        Forbidden.Contains(name, StringComparer.OrdinalIgnoreCase)
        || name.StartsWith("delete", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("drop", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a role may approve. Approving a change the assistant proposed is
    /// a supervisory act — an Operation User approving the assistant's edit to
    /// their own job would be the assistant editing it.
    /// </summary>
    public static bool CanApprove(string role) => StaffDirectory.IsSupervisor(role);
}
