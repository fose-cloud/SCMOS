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
public class JobsRepository(ScmosDbContext db, JobRegisterCache register)
{
    /// <summary>What one save may carry. The workspace never sends more in one go.</summary>
    public const int Limit = 5000;

    /// <summary>Operator name to owner id, read once per save. See SaveAsync.</summary>
    private Dictionary<string, string> _directory = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The owner id for an operator name off a plan workbook, or empty when the
    /// directory has never heard of them — an unassigned job is a visible
    /// problem, a misassigned one is not.
    /// </summary>
    private string IdForName(string? name)
    {
        var first = (name ?? "").Trim()
            .Split([' ', '	'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is not null && _directory.TryGetValue(first, out var id) ? id : "";
    }

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
        var snapshot = await register.ReadAsync(token);
        return (snapshot.Json, snapshot.Count);
    }

    /// <summary>
    /// What the register currently says about these jobs, keyed by job key.
    ///
    /// Read before a save so the audit trail can record what a field changed
    /// *from*. Only the fields worth auditing are pulled back, and only for the
    /// keys being written — a debounced edit is one or two jobs, so this is a
    /// small read, not a second copy of the register.
    /// </summary>
    public async Task<Dictionary<string, Dictionary<string, string>>> SnapshotAsync(
        IReadOnlyList<string> keys, CancellationToken token)
    {
        var wanted = keys.Select(key => Text(key, 80)).Where(key => key.Length > 0).Distinct().ToList();
        var snapshot = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (wanted.Count == 0) return snapshot;

        var rows = await db.OperationJobs.AsNoTracking()
            .Where(job => wanted.Contains(job.Key))
            .Select(job => new { job.Key, job.Trucker, job.Status, job.Owner, job.OwnerId, job.WorkDate, job.Container, job.Data })
            .ToListAsync(token);

        foreach (var row in rows)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["trucker"] = row.Trucker,
                ["status"] = row.Status,
                ["op"] = row.Owner,
                // Not audited — no rule in AuditActions names it — but carried
                // so the save route can check who owns a job before writing it.
                ["ownerId"] = row.OwnerId,
                ["date"] = row.WorkDate,
                ["container"] = row.Container,
            };

            // licence, driver and planTime live in the job's JSON rather than in
            // a column, so they are read from there. A row that will not parse
            // simply contributes no old values — the audit says "—", which is
            // true, rather than failing the save.
            try
            {
                if (JsonNode.Parse(row.Data) is JsonObject job)
                    foreach (var name in new[] { "licence", "driver", "planTime" })
                        fields[name] = job[name]?.ToString() ?? "";
            }
            catch (JsonException) { /* unparseable row: no old values to report */ }

            snapshot[row.Key] = fields;
        }

        return snapshot;
    }

    /// <summary>
    /// Which of these jobs belong to somebody other than <paramref name="ownerId"/>.
    ///
    /// A job with no owner recorded, and a key that is not in the register yet,
    /// both count as available — an unassigned job is a visible problem for a
    /// person to fix, not a locked one, and a new job has no previous owner to
    /// take it from.
    /// </summary>
    /// <param name="alsoFor">
    /// Owners this person is standing in for today, from a delegation. Empty
    /// for almost everybody — it is a holiday arrangement, not a role — and the
    /// caller reads it from <c>DelegationService</c> so the same answer applies
    /// here as on the screen.
    /// </param>
    public async Task<IReadOnlyList<string>> OthersJobsAsync(IReadOnlyList<string> keys, string ownerId,
        CancellationToken token, IReadOnlyList<string>? alsoFor = null)
    {
        var wanted = keys.Select(key => Text(key, 80)).Where(key => key.Length > 0).Distinct().ToList();
        if (wanted.Count == 0) return [];

        var mine = new List<string> { ownerId };
        if (alsoFor is not null) mine.AddRange(alsoFor.Where(id => id.Length > 0));

        return await db.OperationJobs.AsNoTracking()
            .Where(job => wanted.Contains(job.Key) && job.OwnerId != "" && !mine.Contains(job.OwnerId))
            .Select(job => job.Key)
            .ToListAsync(token);
    }

    /// <summary>
    /// Writes whole jobs.
    ///
    /// The batch goes into a temp table in one bulk copy and is then matched
    /// against the register in a single statement, so saving the whole July plan
    /// is two round trips rather than two thousand.
    /// </summary>
    /// <summary>
    /// Changes a few fields on one job, leaving every other field alone.
    ///
    /// The save path takes whole jobs, which is right for the grid — it sends
    /// back what it holds. It is wrong for a caller that knows about three
    /// fields and nothing else: building a whole job around them would write
    /// blanks over everything it did not know, and the register would lose a
    /// column every time a carrier answered the phone. So this reads the row
    /// first and writes it back merged.
    /// </summary>
    public async Task<bool> PatchAsync(string key, IReadOnlyDictionary<string, string> fields,
        string by, CancellationToken token)
    {
        var wanted = Text(key, 80);
        if (wanted.Length == 0 || fields.Count == 0) return false;

        var row = await db.OperationJobs.AsNoTracking()
            .FirstOrDefaultAsync(job => job.Key == wanted, token);
        if (row is null) return false;

        var merged = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.Data)
                     ?? new Dictionary<string, JsonElement>();
        foreach (var (name, value) in fields)
        {
            merged[name] = JsonSerializer.SerializeToElement(value);
        }
        merged["key"] = JsonSerializer.SerializeToElement(row.Key);

        // Recording why is the caller's job, through AuditService — it knows
        // the role, the address and the session, and writing a second, thinner
        // audit row here would be the same rule kept in two places.
        var (saved, _) = await SaveAsync([JsonSerializer.SerializeToElement(merged)], by, token);
        return saved > 0;
    }

    public async Task<(int Saved, DateTimeOffset At)> SaveAsync(IReadOnlyList<JsonElement> jobs, string by, CancellationToken token)
    {
        // One read of the directory for the whole batch. A per-row lookup would
        // be two thousand queries to answer the same question two thousand times.
        _directory = await db.Staff.AsNoTracking()
            .Select(person => new { person.Id, person.Name })
            .ToDictionaryAsync(person => person.Name, person => person.Id, StringComparer.OrdinalIgnoreCase, token);

        var now = DateTimeOffset.UtcNow;
        var table = BuildTable(jobs, by, now);
        if (table.Rows.Count == 0) return (0, now);

        // The configured SQL execution strategy also has to wrap this manual
        // ADO.NET path; EnableRetryOnFailure does not retry SqlBulkCopy by itself.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
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
        });

        register.Invalidate();
        return (table.Rows.Count, now);
    }

    /// <summary>
    /// The jobs whose plan changed: cancelled, or moved off the date they were
    /// first planned for.
    ///
    /// Filtered in SQL rather than in memory, which is the whole point of it
    /// existing. The obvious way to answer this was to ask the workspace's paging
    /// endpoint for the CANCEL / MOVED tab, and that endpoint reads the entire
    /// register — every job, two and a half megabytes of JSON — parses it, and
    /// counts all nine tabs, before returning the handful of rows the screen
    /// wanted. It is the right shape for a grid that draws one tab and needs the
    /// numbers on the other eight; it is the wrong shape for a screen that needs
    /// neither.
    ///
    /// <c>status</c> is a real column, so the cancelled half is an indexed
    /// predicate. <c>origDate</c> lives inside the stored JSON, so the moved half
    /// goes through JSON_VALUE — which returns null for a row that will not
    /// parse, exactly as the rest of this class skips those rather than failing
    /// the read.
    /// </summary>
    public async Task<(string Json, int Count)> ChangedAsync(CancellationToken token)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(token);

        var rows = new List<string>();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 120;
            command.CommandText = """
                SELECT data
                FROM operation_jobs
                WHERE status = @cancelled
                   -- The CASE rather than a bare JSON_VALUE: a row whose data
                   -- will not parse makes JSON_VALUE raise, and this class has
                   -- always skipped those rather than failing the whole read.
                   -- CASE is the one form whose evaluation order is guaranteed.
                   OR ISNULL(CASE WHEN ISJSON(data) = 1
                                  THEN JSON_VALUE(data, '$.origDate') END, '') <> ''
                ORDER BY CASE WHEN work_date = '' THEN 1 ELSE 0 END, work_date DESC, [key]
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@cancelled";
            parameter.Value = "CANCELLED";
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var data = reader.GetString(0);
                if (IsWellFormed(data)) rows.Add(data);
            }
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }

        var builder = new StringBuilder(rows.Count * 900 + 64);
        builder.Append("{\"jobs\":[");
        for (var index = 0; index < rows.Count; index++)
        {
            if (index > 0) builder.Append(',');
            builder.Append(rows[index]);
        }
        builder.Append("],\"count\":").Append(rows.Count).Append('}');

        return (builder.ToString(), rows.Count);
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
        if (removed > 0) register.Invalidate();
        return removed;
    }

    public async Task<int> ClearAsync(CancellationToken token)
    {
        var removed = await db.OperationJobs.ExecuteDeleteAsync(token);
        if (removed > 0) register.Invalidate();
        return removed;
    }

    private static async Task Execute(SqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync(token);
    }

    private DataTable BuildTable(IReadOnlyList<JsonElement> jobs, string by, DateTimeOffset now)
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
            var ownerId = suppliedOwnerId.Length > 0 ? suppliedOwnerId : IdForName(owner);

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

}
