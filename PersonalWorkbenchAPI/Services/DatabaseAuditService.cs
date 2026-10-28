using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace PersonalWorkbench.Services;

/// <summary>审计存储独立于业务库；先持久化执行意图，再执行 SQL。审计存储本身不递归审计。</summary>
public sealed class DatabaseAuditService
{
    private readonly string connectionString;
    private readonly SemaphoreSlim gate = new(1, 1);

    public DatabaseAuditService(IWebHostEnvironment environment)
    {
        var directory = Path.Combine(environment.ContentRootPath, "Data");
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "database-audit.db"), Pooling = false }.ToString();
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS sql_logs (id INTEGER PRIMARY KEY AUTOINCREMENT, started_at TEXT NOT NULL, request_id TEXT, actor TEXT, operation TEXT NOT NULL, sql TEXT NOT NULL, parameters TEXT NOT NULL, status TEXT NOT NULL, duration_ms INTEGER, affected_rows INTEGER, error TEXT);";
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    public async Task<long> BeginAsync(string sql, Dictionary<string, object?> parameters, string operation, HttpContext? context)
    {
        await gate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO sql_logs (started_at,request_id,actor,operation,sql,parameters,status) VALUES ($time,$request,$actor,$operation,$sql,$parameters,'running') RETURNING id;";
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$request", (object?)context?.TraceIdentifier ?? DBNull.Value);
            command.Parameters.AddWithValue("$actor", (object?)context?.Connection.RemoteIpAddress?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$operation", operation);
            command.Parameters.AddWithValue("$sql", sql);
            command.Parameters.AddWithValue("$parameters", JsonSerializer.Serialize(parameters, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return (long)(await command.ExecuteScalarAsync())!;
        }
        finally { gate.Release(); }
    }

    public async Task FinishAsync(long id, long duration, int? affected, string? error)
    {
        await gate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE sql_logs SET status=$status,duration_ms=$duration,affected_rows=$affected,error=$error WHERE id=$id;";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$status", error is null ? "success" : "failed");
            command.Parameters.AddWithValue("$duration", duration);
            command.Parameters.AddWithValue("$affected", (object?)affected ?? DBNull.Value);
            command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }
        finally { gate.Release(); }
    }

    public async Task<object> ListAsync(int page, int pageSize, string? search, string? status)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw new ArgumentException("日志分页参数无效。");
        await gate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            var where = " WHERE ($search='' OR instr(lower(sql || parameters || operation || coalesce(error,'') || coalesce(request_id,'') || coalesce(actor,'')),lower($search))>0) AND ($status='' OR status=$status)";
            command.Parameters.AddWithValue("$search", search?.Trim() ?? "");
            command.Parameters.AddWithValue("$status", status ?? "");
            command.CommandText = "SELECT count(*) FROM sql_logs" + where;
            var total = (long)(await command.ExecuteScalarAsync())!;
            command.CommandText = "SELECT * FROM sql_logs" + where + " ORDER BY id DESC LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$limit", pageSize);
            command.Parameters.AddWithValue("$offset", (long)(page - 1) * pageSize);
            var items = new List<Dictionary<string, object?>>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                items.Add(Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, i => reader.IsDBNull(i) ? null : reader.GetValue(i)));
            return new { items, total, page, pageSize };
        }
        finally { gate.Release(); }
    }
}
