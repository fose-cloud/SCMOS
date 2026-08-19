namespace Scmos.Api.Rules;

/// <summary>
/// What a role may do.
///
/// Capabilities rather than role-name comparisons. The system had grown a
/// <c>SupervisorRoles</c> array that four different files tested against, which
/// meant "who may approve an AI change" and "who may reassign a job" were the
/// same question by accident rather than by decision — and adding a ninth role
/// would have silently granted it whatever the array happened to contain.
///
/// A capability is a question the code actually asks. If nothing asks it, it
/// does not belong here.
/// </summary>
[Flags]
public enum Capability
{
    None = 0,

    /// <summary>See the executive figures. Nearly everyone has this.</summary>
    ViewDashboard = 1 << 0,

    /// <summary>See the whole team's jobs, not only your own.</summary>
    ViewTeam = 1 << 1,

    /// <summary>Edit the jobs assigned to you.</summary>
    EditOwnJobs = 1 << 2,

    /// <summary>Edit anybody's job.</summary>
    EditAnyJob = 1 << 3,

    /// <summary>Hand a job to a different operator.</summary>
    AssignJobs = 1 << 4,

    /// <summary>Attach documents.</summary>
    UploadDocuments = 1 << 5,

    /// <summary>See what carriers charge. Commercial-in-confidence.</summary>
    ViewRates = 1 << 6,

    /// <summary>Change a quoted rate.</summary>
    EditRates = 1 << 7,

    /// <summary>Register a vendor and move them through onboarding.</summary>
    ManageSuppliers = 1 << 8,

    /// <summary>Sign off a CAR/PAR.</summary>
    CloseCarPar = 1 << 9,

    /// <summary>Agree to a change the assistant proposed.</summary>
    ApproveAi = 1 << 10,

    /// <summary>Read the audit trail.</summary>
    ViewAudit = 1 << 11,

    /// <summary>Agree that a document past its retention may be destroyed.</summary>
    ApproveRetention = 1 << 12,

    /// <summary>Replace the register wholesale, run the cleanup pass.</summary>
    AdministerData = 1 << 13,

    /// <summary>
    /// Put a driver on a customer's work when their training does not allow it.
    ///
    /// The block is the point of the training module, so this is the exception
    /// to it — and every use is recorded with a reason and a name. Held from
    /// Operation User upward, which is the team that arranges carriers and the
    /// only one that can judge whether a substitute driver is acceptable.
    ///
    /// Deliberately not held by the <see cref="Roles.Subcontractor"/> role. That
    /// account belongs to the carrier, and letting a carrier wave their own
    /// untrained driver onto a customer's site is the one thing this control
    /// exists to stop.
    /// </summary>
    OverrideTraining = 1 << 14,
}

/// <param name="Name">The role as it is written on an account.</param>
/// <param name="Scope">What this role is for, in one line — the column in the agreed table.</param>
public record RoleDefinition(string Name, string ScopeEn, string ScopeTh, Capability Grants);

public static class Roles
{
    public const string Admin = "Administrator";
    public const string Manager = "Manager";
    public const string AssistantManager = "Assistant Manager";
    public const string Supervisor = "Operation Supervisor";
    public const string Subcontractor = "Subcontractor";
    public const string Operation = "Operation User";
    public const string CustomerService = "CS";
    public const string Management = "Management";
    public const string Viewer = "Viewer";

    private const Capability Read = Capability.ViewDashboard | Capability.ViewTeam;

    private const Capability OperationGrants =
        Read | Capability.EditOwnJobs | Capability.UploadDocuments | Capability.ViewRates
        | Capability.OverrideTraining;

    private const Capability SupervisorGrants =
        OperationGrants | Capability.EditAnyJob | Capability.AssignJobs | Capability.CloseCarPar
        | Capability.ApproveAi | Capability.ManageSuppliers | Capability.ViewAudit;

    private const Capability ManagerGrants =
        SupervisorGrants | Capability.EditRates | Capability.ApproveRetention;

    /// <summary>
    /// The agreed roles.
    ///
    /// Order matters only for display; nothing infers seniority from position,
    /// because "is this role above that one" is exactly the reasoning that
    /// produced the SupervisorRoles array.
    /// </summary>
    public static readonly RoleDefinition[] All =
    [
        new(Admin, "Full System", "ดูแลระบบทั้งหมด",
            ManagerGrants | Capability.AdministerData),

        new(Manager, "Department Overview", "ภาพรวมทั้งแผนก",
            ManagerGrants),

        new(AssistantManager, "Department Overview", "ภาพรวมทั้งแผนก",
            ManagerGrants),

        new(Supervisor, "Team Control", "ควบคุมงานของทีม",
            SupervisorGrants),

        // A carrier signing in to work their own jobs. They may not see the rate
        // book: it holds seventeen other carriers' negotiated prices, and one
        // subcontractor reading another's rates is the single worst thing this
        // system could leak.
        new(Subcontractor, "Operational Management", "จัดการงานที่ได้รับมอบหมาย",
            Capability.ViewDashboard | Capability.EditOwnJobs | Capability.UploadDocuments),

        new(Operation, "Own Workspace", "พื้นที่งานของตัวเอง",
            OperationGrants),

        // Customer service see the work and attach paperwork, but do not key the
        // operational record — the operator who owns the job does.
        new(CustomerService, "View / Upload", "ดูและอัปโหลดเอกสาร",
            Read | Capability.UploadDocuments),

        new(Management, "Dashboard", "แดชบอร์ดผู้บริหาร",
            Capability.ViewDashboard),

        new(Viewer, "Read Only", "ดูอย่างเดียว",
            Read),
    ];

    public static RoleDefinition? Find(string? role) =>
        All.FirstOrDefault(entry => entry.Name.Equals((role ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What this role may do. An unrecognised role gets <see cref="Viewer"/>'s
    /// grants, not the default role's — a typo in a role claim should cost
    /// somebody their edit rights, never hand them somebody else's.
    /// </summary>
    public static Capability GrantsOf(string? role) =>
        Find(role)?.Grants ?? Find(Viewer)!.Grants;

    public static bool Can(string? role, Capability capability) =>
        (GrantsOf(role) & capability) == capability;

    /// <summary>
    /// The old question, kept as one named thing rather than four copies of an
    /// array test. It means "may approve on the team's behalf" — which is what
    /// every caller of the old <c>SupervisorRoles</c> was actually asking.
    /// </summary>
    public static bool IsSupervisor(string? role) => Can(role, Capability.ApproveAi);
}
