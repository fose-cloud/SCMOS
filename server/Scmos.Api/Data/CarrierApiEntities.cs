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

/// <summary>
/// One write a carrier's TMS made, by its Idempotency-Key: what the request
/// was (its hash) and what SCMOS answered, so a retry of the same request
/// gets the same answer and never a second acceptance. A row with no
/// answer yet is a request still being processed. Kept for
/// <see cref="Rules.CarrierApi.IdempotencyDays"/>, then swept by the next
/// write from that client.
/// </summary>
public class CarrierApiRequest
{
    public long Id { get; set; }

    /// <summary>The key's row — <c>carrier_api_clients.id</c>. A key is scoped to its client; two clients may use the same key text.</summary>
    public long ClientRowId { get; set; }

    public string IdempotencyKey { get; set; } = "";

    /// <summary>SHA-256 of method, path and body — <see cref="Rules.CarrierApi.RequestHash"/>.</summary>
    public string RequestHash { get; set; } = "";

    /// <summary>The HTTP status answered, or 0 while the request is being processed.</summary>
    public int ResponseStatus { get; set; }

    public string ResponseContentType { get; set; } = "";

    public string ResponseBody { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public static void Configure(ModelBuilder model)
    {
        model.Entity<CarrierApiRequest>(entry =>
        {
            entry.ToTable("carrier_api_requests");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.ClientRowId).HasColumnName("client_row_id");
            entry.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
            entry.Property(e => e.RequestHash).HasColumnName("request_hash").HasMaxLength(64);
            entry.Property(e => e.ResponseStatus).HasColumnName("response_status");
            entry.Property(e => e.ResponseContentType).HasColumnName("response_content_type").HasMaxLength(60).HasDefaultValue("");
            entry.Property(e => e.ResponseBody).HasColumnName("response_body").HasDefaultValue("");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.CompletedAt).HasColumnName("completed_at");
            // One row per key per client, enforced by the database: two
            // requests racing with one key cannot both be "first".
            entry.HasIndex(e => new { e.ClientRowId, e.IdempotencyKey }).IsUnique().HasDatabaseName("carrier_api_requests_key_idx");
            entry.HasIndex(e => e.CreatedAt).HasDatabaseName("carrier_api_requests_created_idx");
        });
    }
}
