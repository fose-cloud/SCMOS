using Microsoft.EntityFrameworkCore;

namespace Scmos.Api.Data;

/// <summary>Immutable metadata events. No prompt, raw arguments, provider output or hidden reasoning.</summary>
public sealed class AiAuditLog
{
    public long Id { get; set; }
    public string RunId { get; set; } = "";
    public int Sequence { get; set; }
    public string UserId { get; set; } = "";
    public string Role { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string Event { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public bool TeamScope { get; set; }
    public string? OperatorId { get; set; }
    public string? Tool { get; set; }
    public string? ToolCallId { get; set; }
    public string Model { get; set; } = "";
    public string? View { get; set; }
    public int? Limit { get; set; }
    public int? Total { get; set; }
    public int? Returned { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string SourceKeys { get; set; } = "[]";
    public string Risk { get; set; } = "low";
    public string ApprovalStatus { get; set; } = "not_required";
    public string Source { get; set; } = "operation_jobs";
    public string Fingerprint { get; set; } = "";

    public static void Configure(ModelBuilder model)
    {
        model.Entity<AiAuditLog>(entry =>
        {
            entry.ToTable("ai_audit_logs");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.RunId).HasColumnName("run_id").HasMaxLength(32);
            entry.Property(e => e.Sequence).HasColumnName("sequence");
            entry.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(160);
            entry.Property(e => e.Role).HasColumnName("user_role").HasMaxLength(60);
            entry.Property(e => e.AgentId).HasColumnName("agent_id").HasMaxLength(40);
            entry.Property(e => e.Event).HasColumnName("event").HasMaxLength(20);
            entry.Property(e => e.Status).HasColumnName("status").HasMaxLength(32);
            entry.Property(e => e.At).HasColumnName("created_at");
            entry.Property(e => e.TeamScope).HasColumnName("team_scope");
            entry.Property(e => e.OperatorId).HasColumnName("operator_id").HasMaxLength(20);
            entry.Property(e => e.Tool).HasColumnName("tool_name").HasMaxLength(60);
            entry.Property(e => e.ToolCallId).HasColumnName("tool_call_id").HasMaxLength(32);
            entry.Property(e => e.Model).HasColumnName("model").HasMaxLength(100);
            entry.Property(e => e.View).HasColumnName("view").HasMaxLength(20);
            entry.Property(e => e.Limit).HasColumnName("result_limit");
            entry.Property(e => e.Total).HasColumnName("total");
            entry.Property(e => e.Returned).HasColumnName("returned");
            entry.Property(e => e.InputTokens).HasColumnName("input_tokens");
            entry.Property(e => e.OutputTokens).HasColumnName("output_tokens");
            entry.Property(e => e.SourceKeys).HasColumnName("source_keys").HasMaxLength(6000);
            entry.Property(e => e.Risk).HasColumnName("risk").HasMaxLength(12);
            entry.Property(e => e.ApprovalStatus).HasColumnName("approval_status").HasMaxLength(20);
            entry.Property(e => e.Source).HasColumnName("source").HasMaxLength(40);
            entry.Property(e => e.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);
            entry.HasIndex(e => new { e.RunId, e.Sequence }).IsUnique().HasDatabaseName("ai_audit_logs_run_sequence_idx");
            entry.HasIndex(e => new { e.At, e.Id }).HasDatabaseName("ai_audit_logs_at_idx");
        });
    }
}
