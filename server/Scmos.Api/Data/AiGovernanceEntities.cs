using Microsoft.EntityFrameworkCore;

namespace Scmos.Api.Data;

/// <summary>
/// One agent's governance settings — autonomy, shadow mode, status — or, in
/// the row whose id is <c>platform</c>, the platform's ceiling (the execution
/// kill switch). No row means the code's defaults, which are exactly how every
/// agent behaved before this table existed. Changed only by an administrator,
/// with a reason, audited; the revision refuses a change made against a stale
/// read.
/// </summary>
public sealed class AiAgentConfig
{
    public string AgentId { get; set; } = "";
    public int Autonomy { get; set; }
    public bool ShadowMode { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public string Reason { get; set; } = "";
    public int Revision { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>
    /// The agent's on/off switch in the AI Control Tower (29 Sep 2026): null follows its flag in
    /// configuration, as every agent did before; true or false is an administrator's choice and wins.
    /// </summary>
    public bool? Enabled { get; set; }
    /// <summary>The same for the agent's own scheduled pass where it has a separate one — the Communication Agent's drafts, the Booking Agent's mail.</summary>
    public bool? PassEnabled { get; set; }

    public static void Configure(ModelBuilder model)
    {
        model.Entity<AiAgentConfig>(entry =>
        {
            entry.ToTable("ai_agent_settings");
            entry.HasKey(e => e.AgentId);
            entry.Property(e => e.AgentId).HasColumnName("agent_id").HasMaxLength(40);
            entry.Property(e => e.Autonomy).HasColumnName("autonomy");
            entry.Property(e => e.ShadowMode).HasColumnName("shadow_mode");
            entry.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entry.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(200);
            entry.Property(e => e.Revision).HasColumnName("revision").IsConcurrencyToken();
            entry.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(160);
            entry.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entry.Property(e => e.Enabled).HasColumnName("enabled");
            entry.Property(e => e.PassEnabled).HasColumnName("pass_enabled");
            entry.ToTable(table =>
            {
                table.HasCheckConstraint("ai_agent_settings_autonomy_ck", "[autonomy] BETWEEN 0 AND 4");
                table.HasCheckConstraint("ai_agent_settings_status_ck", "[status] IN ('ACTIVE','PAUSED','MAINTENANCE','DISABLED')");
            });
        });
    }
}

/// <summary>
/// What an agent concluded about one thing, and what a person then did —
/// the decision log (AI Agent Platform specification §17, §24). The AI audit
/// keeps what happened in a run; this keeps what the run concluded, with its
/// facts, rule results, observations, inferences and recommendations kept
/// apart, and — for a shadow run — the human's own choice beside the agent's,
/// so autonomy is only ever raised on a record of agreement.
/// </summary>
public sealed class AiDecision
{
    public long Id { get; set; }
    /// <summary>The audited run that reached it; empty when a deterministic pass made it without a model.</summary>
    public string RunId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string DecisionType { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    /// <summary>The job owner's operator id when the entity is a job — who may answer it besides a supervisor.</summary>
    public string OwnerId { get; set; } = "";
    public string Summary { get; set; } = "";
    /// <summary>COMPLETED · UNKNOWN · INSUFFICIENT_INFORMATION · REQUIRES_HUMAN_REVIEW · BLOCKED.</summary>
    public string ResultStatus { get; set; } = "";
    /// <summary>
    /// OPEN until a person answers — ACCEPTED, OVERRIDDEN or DISMISSED — or the
    /// agent's next pass closes it: SUPERSEDED when it concluded something else
    /// about the same thing, RESOLVED when there was nothing left to conclude.
    /// </summary>
    public string Status { get; set; } = "OPEN";

    /// <summary>What the conclusion was, without its moving figures — the kind, the outcome, the rules that fired — so a pass can tell "the same finding again" from "a different one".</summary>
    public string Fingerprint { get; set; } = "";
    public string RiskLevel { get; set; } = "";
    public decimal? Confidence { get; set; }
    public bool RequiresApproval { get; set; }
    public bool Shadow { get; set; }
    public int Autonomy { get; set; }
    public string PromptVersion { get; set; } = "";
    /// <summary>The findings as JSON — facts, rule results, observations, inferences, recommendations, blocking issues.</summary>
    public string Payload { get; set; } = "{}";
    public string RuleReferences { get; set; } = "[]";
    public string EvidenceReferences { get; set; } = "[]";
    public string HumanChoice { get; set; } = "";
    public bool? HumanMatches { get; set; }
    public string OverrideReason { get; set; } = "";
    public string DecidedById { get; set; } = "";
    public string DecidedBy { get; set; } = "";
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static void Configure(ModelBuilder model)
    {
        model.Entity<AiDecision>(entry =>
        {
            entry.ToTable("ai_decisions");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.RunId).HasColumnName("run_id").HasMaxLength(32);
            entry.Property(e => e.AgentId).HasColumnName("agent_id").HasMaxLength(40);
            entry.Property(e => e.DecisionType).HasColumnName("decision_type").HasMaxLength(40);
            entry.Property(e => e.EntityType).HasColumnName("entity_type").HasMaxLength(20);
            entry.Property(e => e.EntityId).HasColumnName("entity_id").HasMaxLength(80);
            entry.Property(e => e.OwnerId).HasColumnName("owner_id").HasMaxLength(20);
            entry.Property(e => e.Summary).HasColumnName("summary").HasMaxLength(400);
            entry.Property(e => e.ResultStatus).HasColumnName("result_status").HasMaxLength(30);
            // Answered once: the update carries "and it is still OPEN", so two people answering at once cannot both win.
            entry.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsConcurrencyToken();
            entry.Property(e => e.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64).HasDefaultValue("");
            entry.Property(e => e.RiskLevel).HasColumnName("risk_level").HasMaxLength(20);
            entry.Property(e => e.Confidence).HasColumnName("confidence").HasPrecision(4, 3);
            entry.Property(e => e.RequiresApproval).HasColumnName("requires_approval");
            entry.Property(e => e.Shadow).HasColumnName("shadow");
            entry.Property(e => e.Autonomy).HasColumnName("autonomy");
            entry.Property(e => e.PromptVersion).HasColumnName("prompt_version").HasMaxLength(60);
            entry.Property(e => e.Payload).HasColumnName("payload");
            entry.Property(e => e.RuleReferences).HasColumnName("rule_references").HasMaxLength(4000);
            entry.Property(e => e.EvidenceReferences).HasColumnName("evidence_references").HasMaxLength(4000);
            entry.Property(e => e.HumanChoice).HasColumnName("human_choice").HasMaxLength(400);
            entry.Property(e => e.HumanMatches).HasColumnName("human_matches");
            entry.Property(e => e.OverrideReason).HasColumnName("override_reason").HasMaxLength(400);
            entry.Property(e => e.DecidedById).HasColumnName("decided_by_id").HasMaxLength(160);
            entry.Property(e => e.DecidedBy).HasColumnName("decided_by").HasMaxLength(160);
            entry.Property(e => e.DecidedAt).HasColumnName("decided_at");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.HasIndex(e => new { e.EntityType, e.EntityId, e.CreatedAt }).HasDatabaseName("ai_decisions_entity_idx");
            entry.HasIndex(e => new { e.AgentId, e.CreatedAt }).HasDatabaseName("ai_decisions_agent_idx");
            entry.HasIndex(e => new { e.Status, e.OwnerId }).HasDatabaseName("ai_decisions_open_idx");
            entry.ToTable(table =>
            {
                table.HasCheckConstraint("ai_decisions_status_ck", "[status] IN ('OPEN','ACCEPTED','OVERRIDDEN','DISMISSED','SUPERSEDED','RESOLVED')");
                table.HasCheckConstraint("ai_decisions_confidence_ck", "[confidence] IS NULL OR ([confidence] >= 0 AND [confidence] <= 1)");
            });
        });
    }
}
