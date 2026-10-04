using Microsoft.EntityFrameworkCore;

namespace Scmos.Api.Data;

/// <summary>Append-only authorization/security evidence, separate from a successful run's event sequence.</summary>
public sealed class AiAuthorizationLog
{
    public string Id { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string AgentId { get; set; } = "";
    public string PolicyVersion { get; set; } = "";
    public string Decision { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public bool SecurityEvent { get; set; }
    public decimal ReservedCost { get; set; }
    public string Metadata { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public static void Configure(ModelBuilder model) => model.Entity<AiAuthorizationLog>(row =>
    {
        row.ToTable("ai_authorization_logs");
        row.HasKey(x => x.Id);
        row.Property(x => x.Id).HasMaxLength(32).ValueGeneratedNever();
        row.Property(x => x.AgentId).HasMaxLength(40);
        row.Property(x => x.PolicyVersion).HasMaxLength(60);
        row.Property(x => x.Decision).HasMaxLength(24);
        row.Property(x => x.ReasonCode).HasMaxLength(60);
        row.Property(x => x.ReservedCost).HasPrecision(18, 6);
        row.Property(x => x.Fingerprint).HasMaxLength(64);
        row.HasIndex(x => new { x.AgentId, x.At });
        row.HasIndex(x => new { x.SecurityEvent, x.At });
    });
}
