using Microsoft.EntityFrameworkCore;

namespace Scmos.Api.Data;

/// <summary>One durable, initially disabled switch. Not an authorization to write jobs.</summary>
public sealed class AiOperationsControl
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    public int Revision { get; set; }

    public static void Configure(ModelBuilder model)
    {
        model.Entity<AiOperationsControl>(entry =>
        {
            entry.ToTable("ai_operations_control", table => table.HasCheckConstraint("CK_ai_operations_control_singleton", "[id] = 1"));
            entry.HasKey(row => row.Id);
            entry.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entry.Property(row => row.Enabled).HasColumnName("enabled");
            entry.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
            entry.HasData(new AiOperationsControl { Id = 1, Enabled = false, Revision = 0 });
        });
    }
}
