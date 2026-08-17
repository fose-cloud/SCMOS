using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;

namespace Scmos.Api.Data;

/// <summary>
/// The register, read and written.
///
/// The workspace holds the job model; this only persists it. Queryable fields
/// become columns so the register can be reported on in SQL, and the job itself
/// rides along as JSON — the two are written together, so they cannot drift.
/// One row per job key, so every save is an upsert and re-saving the same job
/// twice is harmless.
/// </summary>
public class JobsRepository(ScmosDbContext db)
{
    /// <summary>What one save may carry. The workspace never sends more in one go.</summary>
    public const int Limit = 5000;

    /// <summary>
    /// The plan, as JSON, ready to send.
    ///
    /// Each row's stored <c>data</c> is checked for well-formedness and then
    /// written through verbatim — parsing 2,000 jobs only to serialise them
    /// again would double the work for no gain. A row that will not parse is
    /// skipped rather than breaking the load, as it always was.
    /// </summary>
    public async Task<(string Json, int Count)> LoadAsync(CancellationToken token)
    {
        // Rows with no usable date sort last. They used to lead every list — an
        // empty string sorts before every date — so the grid opened on the least
        // useful rows in the register.
        var rows = await db.OperationJobs
            .AsNoTracking()
            .OrderBy(job => job.WorkDate == "" ? 1 : 0)
            .ThenBy(job => job.WorkDate)
            .ThenBy(job => job.Key)
            .Take(Limit)
            .Select(job => new { job.Data, job.UpdatedAt })
            .ToListAsync(token);

        var builder = new StringBuilder(rows.Count * 900);
        builder.Append("{\"jobs\":[");

        var count = 0;
        DateTimeOffset updatedAt = default;
        foreach (var row in rows)
        {
            if (!IsWellFormed(row.Data)) continue;
            if (count > 0) builder.Append(',');
            builder.Append(row.Data);
            if (row.UpdatedAt > updatedAt) updatedAt = row.UpdatedAt;
            count++;
        }

        builder.Append("],\"count\":").Append(count).Append(",\"updatedAt\":");
        builder.Append(JsonSerializer.Serialize(count == 0 ? "" : Iso(updatedAt)));
        builder.Append('}');

        return (builder.ToString(), count);
    }

    /// <summary>
    /// Writes whole jobs.
    ///
    /// The batch goes into a temp table in one bulk copy and is then matched
    /// against the register in a single statement, so saving the whole July plan
    /// is two round trips rather than two thousand.
    /// </summary>
    public async Task<(int Saved, DateTimeOffset At)> SaveAsync(IReadOnlyList<JsonElement> jobs, string by, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var table = BuildTable(jobs, by, now);
        if (table.Rows.Count == 0) return (0, now);

        var connection = (SqlConnection)db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(token);

        try
        {
            await Execute(connection, """
                CREATE TABLE #incoming (
                  [key] NVARCHAR(80) NOT NULL PRIMARY KEY,
                  cat NVARCHAR(20) NOT NULL, owner NVARCHAR(60) NOT NULL, owner_id NVARCHAR(20) NOT NULL,
                  work_date NVARCHAR(20) NOT NULL, customer NVARCHAR(200) NOT NULL, trucker NVARCHAR(200) NOT NULL,
                  job_code NVARCHAR(80) NOT NULL, container NVARCHAR(40) NOT NULL, status NVARCHAR(60) NOT NULL,
                  data NVARCHAR(MAX) NOT NULL, updated_by NVARCHAR(120) NOT NULL, updated_at DATETIMEOFFSET NOT NULL)
                """, token);

            using (var bulk = new SqlBulkCopy(connection) { DestinationTableName = "#incoming", BulkCopyTimeout = 120 })
            {
                foreach (DataColumn column in table.Columns)
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                await bulk.WriteToServerAsync(table, token);
            }

            // UPDATE then INSERT rather than MERGE: the same result, without
            // MERGE's long tail of concurrency bugs on SQL Server.
            await Execute(connection, """
                UPDATE target SET
                  cat = source.cat, owner = source.owner, owner_id = source.owner_id,
                  work_date = source.work_date, customer = source.customer, trucker = source.trucker,
                  job_code = source.job_code, container = source.container, status = source.status,
                  data = source.data, updated_by = source.updated_by, updated_at = source.updated_at
                FROM operation_jobs AS target
                INNER JOIN #incoming AS source ON source.[key] = target.[key];

                INSERT INTO operation_jobs ([key], cat, owner, owner_id, work_date, customer, trucker,
                                            job_code, container, status, data, updated_by, updated_at)
                SELECT source.[key], source.cat, source.owner, source.owner_id, source.work_date,
                       source.customer, source.trucker, source.job_code, source.container, source.status,
                       source.data, source.updated_by, source.updated_at
                FROM #incoming AS source
                WHERE NOT EXISTS (SELECT 1 FROM operation_jobs AS target WHERE target.[key] = source.[key]);

                DROP TABLE #incoming;
                """, token);
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }

        return (table.Rows.Count, now);
    }

    public async Task<int> DeleteAsync(IReadOnlyList<string> keys, CancellationToken token)
    {
        var wanted = keys.Select(key => Text(key, 80)).Where(key => key.Length > 0).Distinct().ToList();
        if (wanted.Count == 0) return 0;

        var removed = 0;
        // Chunked well under SQL Server's 2,100-parameter ceiling.
        foreach (var chunk in wanted.Chunk(1000))
        {
            removed += await db.OperationJobs
                .Where(job => chunk.Contains(job.Key))
                .ExecuteDeleteAsync(token);
        }
        return removed;
    }

    public Task<int> ClearAsync(CancellationToken token) => db.OperationJobs.ExecuteDeleteAsync(token);

    private static async Task Execute(SqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync(token);
    }

    private static DataTable BuildTable(IReadOnlyList<JsonElement> jobs, string by, DateTimeOffset now)
    {
        var table = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
                 {
                     ("key", typeof(string)), ("cat", typeof(string)), ("owner", typeof(string)),
                     ("owner_id", typeof(string)), ("work_date", typeof(string)), ("customer", typeof(string)),
                     ("trucker", typeof(string)), ("job_code", typeof(string)), ("container", typeof(string)),
                     ("status", typeof(string)), ("data", typeof(string)), ("updated_by", typeof(string)),
                     ("updated_at", typeof(DateTimeOffset)),
                 })
            table.Columns.Add(name, type);

        // A job key can only appear once in a bulk copy into a keyed temp table.
        // The last copy wins, which is what saving the same job twice in one
        // batch has always meant.
        var seen = new Dictionary<string, DataRow>(StringComparer.Ordinal);

        foreach (var job in jobs)
        {
            if (job.ValueKind != JsonValueKind.Object) continue;

            var key = Field(job, "key", 80);
            if (key.Length == 0) key = Field(job, "id", 80);
            if (key.Length == 0) continue;

            var owner = Field(job, "op", 60);
            var suppliedOwnerId = Field(job, "opId", 20);
            var ownerId = suppliedOwnerId.Length > 0 ? suppliedOwnerId : StaffDirectory.IdForName(owner);

            // The column and the JSON are written together so they cannot drift.
            // A register keyed before owner ids existed arrives with only a name,
            // and the id derived from it has to land in both — otherwise the row
            // says OP-01 and the job the workspace reads back says nothing.
            var data = suppliedOwnerId.Length > 0 || ownerId.Length == 0
                ? job.GetRawText()
                : WithOwnerId(job, ownerId);

            var jobCode = Field(job, "jobCode", 80);
            if (jobCode.Length == 0) jobCode = Field(job, "abs", 80);
            if (jobCode.Length == 0) jobCode = Field(job, "jobNo", 80);

            var row = seen.TryGetValue(key, out var existing) ? existing : table.NewRow();
            row["key"] = key;
            row["cat"] = Field(job, "cat", 20);
            row["owner"] = owner;
            row["owner_id"] = ownerId;
            row["work_date"] = Field(job, "date", 20);
            row["customer"] = Field(job, "customer", 200);
            row["trucker"] = Field(job, "trucker", 200);
            row["job_code"] = jobCode;
            row["container"] = Field(job, "container", 40);
            row["status"] = Field(job, "status", 60);
            row["data"] = data;
            row["updated_by"] = Text(by, 120);
            row["updated_at"] = now;

            if (existing is null)
            {
                table.Rows.Add(row);
                seen[key] = row;
            }
        }

        return table;
    }

    /// <summary>
    /// The job with an <c>opId</c> added. Everything else is left exactly as it
    /// arrived — this is the workspace's model, and nothing here understands it
    /// well enough to tidy it.
    /// </summary>
    private static string WithOwnerId(JsonElement job, string ownerId)
    {
        var node = JsonNode.Parse(job.GetRawText())?.AsObject();
        if (node is null) return job.GetRawText();
        node["opId"] = ownerId;
        return node.ToJsonString();
    }

    /// <summary>
    /// Reads one field off the job as text. A number or a boolean is accepted and
    /// rendered — the workspace keeps everything as strings, but a job that has
    /// been through a hand-edited export may not.
    /// </summary>
    private static string Field(JsonElement job, string name, int max)
    {
        if (!job.TryGetProperty(name, out var value)) return "";
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => "",
        };
        return Text(text, max);
    }

    private static string Text(string? value, int max)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static bool IsWellFormed(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
}
