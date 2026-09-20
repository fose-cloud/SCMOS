using Microsoft.EntityFrameworkCore;

namespace Scmos.Api.Data;

/// <summary>
/// A carrier's machine credential: one row per key, bound to one supplier.
///
/// <para>
/// The key itself is shown once, when it is issued, and never stored — only
/// its SHA-256 (<c>Rules/CarrierApi.HashOf</c>). Which supplier the key speaks
/// for is this row's <see cref="SupplierId"/> and nothing in a request can
/// change that; a carrier's TMS is scoped the way a carrier's person is
/// (<c>CarrierService.CompanyOfAsync</c>). A key is retired by
/// <see cref="RevokedAt"/>, never deleted: the audit rows that name its
/// <see cref="ClientId"/> must keep pointing at something.
/// </para>
/// </summary>
public class CarrierApiClient
{
    public long Id { get; set; }

    /// <summary>The supplier this key speaks for — <c>suppliers.id</c>.</summary>
    public int SupplierId { get; set; }

    /// <summary>What the department calls it: "Shore TMS", "DGT test".</summary>
    public string Name { get; set; } = "";

    /// <summary>The public name in logs, audit rows and the screen — "ck_…". Not a secret.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>SHA-256 of the key, lower-case hex. The key is never here.</summary>
    public string KeyHash { get; set; } = "";

    /// <summary>The prefix and four characters, so two keys of one carrier can be told apart on the screen.</summary>
    public string KeyPrefix { get; set; } = "";

    public string Status { get; set; } = CarrierApiClientStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? RevokedAt { get; set; }
    public string RevokedBy { get; set; } = "";

    /// <summary>The last call that carried this key, kept to the nearest few minutes — see CarrierApiAuth.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    public static void Configure(ModelBuilder model)
    {
        model.Entity<CarrierApiClient>(entry =>
        {
            entry.ToTable("carrier_api_clients");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entry.Property(e => e.Name).HasColumnName("name").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.ClientId).HasColumnName("client_id").HasMaxLength(32);
            entry.Property(e => e.KeyHash).HasColumnName("key_hash").HasMaxLength(64);
            entry.Property(e => e.KeyPrefix).HasColumnName("key_prefix").HasMaxLength(24).HasDefaultValue("");
            entry.Property(e => e.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue(CarrierApiClientStatus.Active);
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entry.Property(e => e.RevokedBy).HasColumnName("revoked_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.LastSeenAt).HasColumnName("last_seen_at");
            // A key is looked up by its hash on every call; the hash is unique
            // by construction and the index is what makes the lookup one seek.
            entry.HasIndex(e => e.KeyHash).IsUnique().HasDatabaseName("carrier_api_clients_key_idx");
            entry.HasIndex(e => e.ClientId).IsUnique().HasDatabaseName("carrier_api_clients_client_idx");
            entry.HasIndex(e => e.SupplierId).HasDatabaseName("carrier_api_clients_supplier_idx");
        });
    }
}

public static class CarrierApiClientStatus
{
    public const string Active = "active";
    public const string Revoked = "revoked";
}
