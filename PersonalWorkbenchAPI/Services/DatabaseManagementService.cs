using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PersonalWorkbench.Data;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

public sealed class DatabaseManagementService(WorkbenchDbContext db, DatabaseAuditService audit, IHttpContextAccessor accessor)
{
    private const int MaxRows = 1000;
    private string operation = "query";
    private static string Q(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Contains('\0')) throw new ArgumentException("名称必须为 1–255 个有效字符。");
        return "\"" + name.Replace("\"", "\"\"") + "\"";
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        var builder = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()) { Pooling = false, DefaultTimeout = 10 };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            await connection.OpenAsync(token);
            // 管理连接不允许附加其他磁盘数据库，审计库不会暴露在此连接中。
            SQLitePCL.raw.sqlite3_set_authorizer(connection.Handle, (SQLitePCL.strdelegate_authorizer)((_, action, _, _, _, _) => action is 24 or 25 ? 1 : 0), null);
            await RunAsync(connection, "PRAGMA foreign_keys=ON;", token);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private async Task<List<DatabaseResult>> RunAsync(SqliteConnection connection, string sql, CancellationToken token, Dictionary<string, object?>? parameters = null)
    {
        parameters ??= new();
        var id = await audit.BeginAsync(sql, parameters, operation, accessor.HttpContext);
        var timer = Stopwatch.StartNew();
        var results = new List<DatabaseResult>();
        int? affected = null;
        string? error = null;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            // SQLite 的执行可能同步阻塞；原生进度回调同时约束递归查询和长语句。
            SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 1000, _ => token.IsCancellationRequested || timer.Elapsed.TotalSeconds > 30 ? 1 : 0, null);
            await using var reader = await command.ExecuteReaderAsync(token);
            do
            {
                var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
                var rows = new List<object?[]>();
                var truncated = false;
                while (await reader.ReadAsync(token))
                {
                    if (rows.Count == MaxRows) { truncated = true; continue; }
                    rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : Encode(reader.GetValue(i))).ToArray());
                }
                results.Add(new(columns, rows, truncated, reader.RecordsAffected));
            } while (await reader.NextResultAsync(token));
            affected = reader.RecordsAffected;
            return results;
        }
        catch (Exception exception) { error = exception.Message; throw; }
        finally
        {
            SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 0, null, null);
            await audit.FinishAsync(id, timer.ElapsedMilliseconds, affected, error);
        }
    }

    private static object Encode(object value) => value switch
    {
        byte[] bytes => new Dictionary<string, string> { ["$type"] = "blob", ["base64"] = Convert.ToBase64String(bytes) },
        long number when number > 9007199254740991 || number < -9007199254740991 => new Dictionary<string, string> { ["$type"] = "integer", ["value"] = number.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        _ => value
    };

    private static object? Decode(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => 1L,
        JsonValueKind.False => 0L,
        JsonValueKind.Number => value.TryGetInt64(out var integer) ? (object)integer : value.GetDouble(),
        JsonValueKind.Object when value.TryGetProperty("$type", out var type) && type.GetString() == "blob" => Convert.FromBase64String(value.GetProperty("base64").GetString()!),
        JsonValueKind.Object when value.TryGetProperty("$type", out var type) && type.GetString() == "integer" => long.Parse(value.GetProperty("value").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new ArgumentException("单元格只支持文本、数值、布尔值、null 或带类型的 BLOB / 大整数。")
    };

    public async Task<object> TablesAsync(CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        var result = (await RunAsync(connection, "SELECT name, type, sql FROM sqlite_schema WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name;", token))[0];
        return result.Rows.Select(row => new { name = row[0], type = row[1], sql = row[2] }).ToArray();
    }

    private async Task<(string Sql, List<DatabaseColumn> Columns, string? RowId, bool View)> SchemaAsync(SqliteConnection connection, string table, CancellationToken token)
    {
        var objects = (await RunAsync(connection, "SELECT sql,type FROM sqlite_schema WHERE name=$name AND type IN ('table','view');", token, new() { ["$name"] = table }))[0];
        if (objects.Rows.Count == 0) throw new ArgumentException("表或视图已不存在，请刷新。");
        var sql = (string?)objects.Rows[0][0] ?? "";
        var view = (string)objects.Rows[0][1]! == "view";
        var parts = !view && !sql.StartsWith("CREATE VIRTUAL", StringComparison.OrdinalIgnoreCase) ? SplitDefinitions(sql) : [];
        var columns = (await RunAsync(connection, $"PRAGMA table_xinfo({Q(table)});", token))[0].Rows.Select(row =>
        {
            var name = (string)row[1]!;
            var definition = parts.FirstOrDefault(part => ReadName(part).Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? "";
            return new DatabaseColumn(Convert.ToInt32(row[0]), name, (string)row[2]!, Convert.ToInt32(row[3]) != 0, row[4]?.ToString(), Convert.ToInt32(row[5]), Convert.ToInt32(row[6]), definition[ReadName(definition).End..].Trim());
        }).ToList();
        var tableInfo = (await RunAsync(connection, "SELECT wr FROM pragma_table_list WHERE name=$name AND schema='main';", token, new() { ["$name"] = table }))[0];
        var rowId = !view && tableInfo.Rows.Count > 0 && Convert.ToInt32(tableInfo.Rows[0][0]) == 0
            ? new[] { "rowid", "_rowid_", "oid" }.FirstOrDefault(name => columns.All(column => !column.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) : null;
        return (sql, columns, rowId, view);
    }

    public async Task<object> StructureAsync(string table, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        var schema = await SchemaAsync(connection, table, token);
        var indexes = await IndexesAsync(connection, table, token);
        return new { schema.Sql, schema.Columns, schema.RowId, schema.View, indexes };
    }

    private async Task<List<DatabaseIndex>> IndexesAsync(SqliteConnection connection, string table, CancellationToken token)
    {
        var rows = (await RunAsync(connection, $"PRAGMA index_list({Q(table)});", token))[0].Rows;
        var indexes = new List<DatabaseIndex>();
        foreach (var row in rows)
        {
            var name = (string)row[1]!;
            var columns = (await RunAsync(connection, $"PRAGMA index_info({Q(name)});", token))[0].Rows.Select(c => (string?)c[2]).ToList();
            var sql = (await RunAsync(connection, "SELECT sql FROM sqlite_schema WHERE type='index' AND name=$name;", token, new() { ["$name"] = name }))[0].Rows.FirstOrDefault()?[0] as string;
            indexes.Add(new(name, Convert.ToInt32(row[2]) != 0, (string)row[3]!, Convert.ToInt32(row[4]) != 0, sql, columns));
        }
        return indexes;
    }

    private async Task SetColumnIndexAsync(SqliteConnection connection, string table, DatabaseColumnRequest column, CancellationToken token)
    {
        if (column.Index is null) return;
        if (column.Index is not ("none" or "index" or "unique")) throw new ArgumentException("索引类型无效。");
        // 只调整独立的、非部分单字段索引，不改变复合索引或表内约束。
        var indexes = (await IndexesAsync(connection, table, token)).Where(i => i.Origin == "c" && !i.Partial && i.Columns.Count == 1 && i.Columns[0] == column.Name).ToList();
        if (indexes.Count == 1 && column.Index != "none" && indexes[0].Unique == (column.Index == "unique")) return;
        foreach (var index in indexes) await RunAsync(connection, $"DROP INDEX {Q(index.Name)};", token);
        if (column.Index != "none")
            await RunAsync(connection, $"CREATE {(column.Index == "unique" ? "UNIQUE " : "")}INDEX {Q("idx_" + Guid.NewGuid().ToString("N"))} ON {Q(table)} ({Q(column.Name)});", token);
    }

    public async Task CreateIndexAsync(string table, DatabaseIndexRequest request, CancellationToken token)
    {
        if (request.Columns is not { Count: > 0 and <= 200 } || request.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Columns.Count)
            throw new ArgumentException("请选择不重复的索引字段。");
        operation = "create-index";
        await using var connection = await OpenAsync(token);
        await AtomicAsync(connection, async () =>
        {
            var schema = await SchemaAsync(connection, table, token);
            if (schema.View || request.Columns.Any(name => !schema.Columns.Any(c => c.Name == name && c.Hidden != 1))) throw new ArgumentException("索引字段不存在或不可用。");
            await RunAsync(connection, $"CREATE {(request.Unique ? "UNIQUE " : "")}INDEX {Q(request.Name)} ON {Q(table)} ({string.Join(",", request.Columns.Select(Q))});", token);
        }, token);
    }

    public async Task DropIndexAsync(string table, string name, CancellationToken token)
    {
        operation = "drop-index";
        await using var connection = await OpenAsync(token);
        await AtomicAsync(connection, async () =>
        {
            var index = (await IndexesAsync(connection, table, token)).SingleOrDefault(i => i.Name == name) ?? throw new ArgumentException("索引已不存在。");
            if (index.Origin != "c") throw new ArgumentException("主键或 UNIQUE 约束的自动索引不能独立删除，请修改相应约束。");
            await RunAsync(connection, $"DROP INDEX {Q(name)};", token);
        }, token);
    }

    public async Task<object> RowsAsync(string table, int page, int pageSize, string? search, string? sort, bool descending, CancellationToken token)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw new ArgumentException("数据分页参数无效。");
        await using var connection = await OpenAsync(token);
        var schema = await SchemaAsync(connection, table, token);
        if (sort is not null && schema.Columns.All(column => column.Name != sort)) throw new ArgumentException("排序字段无效。");
        var parameters = new Dictionary<string, object?>();
        var visible = schema.Columns.Where(column => column.Hidden != 1).ToArray();
        var where = string.IsNullOrEmpty(search) ? "" : " WHERE " + string.Join(" OR ", visible.Select(column => $"instr(lower(CAST({Q(column.Name)} AS TEXT)),lower($search))>0"));
        if (where.Length > 0) parameters["$search"] = search;
        var count = (await RunAsync(connection, $"SELECT count(*) FROM {Q(table)}{where};", token, parameters))[0].Rows[0][0];
        var order = sort is not null ? Q(sort) : schema.RowId is not null ? Q(schema.RowId) : string.Join(",", schema.Columns.Where(c => c.PrimaryKey > 0).OrderBy(c => c.PrimaryKey).Select(c => Q(c.Name)));
        parameters = new(parameters) { ["$limit"] = pageSize, ["$offset"] = (long)(page - 1) * pageSize };
        var select = schema.RowId is null ? "" : Q(schema.RowId) + ",";
        var data = (await RunAsync(connection, $"SELECT {select}{string.Join(",", visible.Select(c => Q(c.Name)))} FROM {Q(table)}{where}{(order.Length > 0 ? " ORDER BY " + order + (descending ? " DESC" : " ASC") : "")} LIMIT $limit OFFSET $offset;", token, parameters))[0];
        return new { total = count, columns = visible, rows = data.Rows.Select(row => new { rowId = schema.RowId is null ? null : row[0] is Dictionary<string, string> integer ? integer["value"] : row[0]?.ToString(), values = visible.Select((c, i) => (c.Name, Value: row[i + (schema.RowId is null ? 0 : 1)])).ToDictionary(c => c.Name, c => c.Value) }), editable = !schema.View && (schema.RowId is not null || schema.Columns.Any(c => c.PrimaryKey > 0)) };
    }

    private async Task AtomicAsync(SqliteConnection connection, Func<Task> action, CancellationToken token)
    {
        await RunAsync(connection, "BEGIN IMMEDIATE;", token);
        try { await action(); await RunAsync(connection, "COMMIT;", token); }
        catch
        {
            // SQLITE_INTERRUPT / ON CONFLICT ROLLBACK 可能已自动回滚；保持最初错误。
            try { await RunAsync(connection, "ROLLBACK;", CancellationToken.None); }
            catch { /* 连接关闭时还会回滚未提交事务；审计故障不能掩盖原始错误。 */ }
            throw;
        }
    }

    public async Task<object> ExecuteAsync(string sql, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 100000) throw new ArgumentException("请输入 SQL，最多 100000 字符。");
        operation = "sql";
        await using var connection = await OpenAsync(token);
        List<DatabaseResult> results = [];
        // 控制台批处理由一个事务管理，拒绝脚本自行提交，避免部分提交。
        await AtomicAsync(connection, async () =>
        {
            SQLitePCL.raw.sqlite3_set_authorizer(connection.Handle, (SQLitePCL.strdelegate_authorizer)((_, action, _, _, _, _) => action is 22 or 24 or 25 or 32 ? 1 : 0), null);
            try { results = await RunAsync(connection, sql, token); }
            finally { SQLitePCL.raw.sqlite3_set_authorizer(connection.Handle, (SQLitePCL.strdelegate_authorizer)((_, action, _, _, _, _) => action is 24 or 25 ? 1 : 0), null); }
        }, token);
        return new { results, maxRows = MaxRows };
    }

    public async Task CreateAsync(DatabaseTableRequest request, CancellationToken token)
    {
        if (request.Columns is not { Count: > 0 and <= 200 }) throw new ArgumentException("请提供 1–200 个字段。");
        if (request.Name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("不能使用 SQLite 保留表名。");
        operation = "create-table";
        await using var connection = await OpenAsync(token);
        await AtomicAsync(connection, async () =>
        {
            await RunAsync(connection, $"CREATE TABLE {Q(request.Name)} ({string.Join(",", request.Columns.Select(c => Q(c.Name) + " " + Definition(c.Definition)))});", token);
            foreach (var column in request.Columns) await SetColumnIndexAsync(connection, request.Name, column, token);
        }, token);
    }

    public async Task RenameAsync(string table, string name, CancellationToken token)
    {
        operation = "rename-table";
        await using var connection = await OpenAsync(token);
        await AtomicAsync(connection, async () => await RunAsync(connection, $"ALTER TABLE {Q(table)} RENAME TO {Q(name)};", token), token);
    }

    public async Task TableActionAsync(string table, bool drop, CancellationToken token)
    {
        operation = drop ? "drop-table" : "clear-table";
        await using var connection = await OpenAsync(token);
        await AtomicAsync(connection, async () => await RunAsync(connection, drop ? $"DROP TABLE {Q(table)};" : $"DELETE FROM {Q(table)};", token), token);
    }

    public async Task ColumnAsync(string table, string? column, DatabaseColumnRequest? request, CancellationToken token)
    {
        operation = column is null ? "add-column" : request is null ? "drop-column" : "modify-column";
        await using var connection = await OpenAsync(token);
        if (request is null)
        {
            await AtomicAsync(connection, async () => await RunAsync(connection, $"ALTER TABLE {Q(table)} DROP COLUMN {Q(column!)};", token), token);
            return;
        }
        await RunAsync(connection, "PRAGMA foreign_keys=OFF;", token);
        await RunAsync(connection, "PRAGMA legacy_alter_table=ON;", token);
        await AtomicAsync(connection, async () =>
        {
            var schema = await SchemaAsync(connection, table, token);
            if (schema.View || schema.Sql.StartsWith("CREATE VIRTUAL", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("此对象不支持重建字段。");
            if (column is not null && schema.Columns.All(c => c.Name != column)) throw new ArgumentException("字段已不存在。");
            // 先由 SQLite 更新索引、触发器、外键等名称引用，再重建字段定义。
            await RunAsync(connection, "PRAGMA legacy_alter_table=OFF;", token);
            if (column is not null && column != request.Name) await RunAsync(connection, $"ALTER TABLE {Q(table)} RENAME COLUMN {Q(column)} TO {Q(request.Name)};", token);
            schema = await SchemaAsync(connection, table, token);
            var parts = SplitDefinitions(schema.Sql);
            var index = parts.FindIndex(part => ReadName(part).Name == request.Name);
            if (column is null)
            {
                if (index >= 0) throw new ArgumentException("字段名已存在。");
                // 字段必须放在表级约束之前。
                var constraintPosition = parts.FindIndex(p => System.Text.RegularExpressions.Regex.IsMatch(p, @"^(?:CONSTRAINT|PRIMARY|UNIQUE|CHECK|FOREIGN)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                parts.Insert(constraintPosition < 0 ? parts.Count : constraintPosition, Q(request.Name) + " " + Definition(request.Definition));
            }
            else
            {
                if (index < 0) throw new ArgumentException("无法解析字段定义，请使用 SQL 控制台。");
                parts[index] = Q(request.Name) + " " + Definition(request.Definition);
            }
            var dependencies = (await RunAsync(connection, "SELECT sql FROM sqlite_schema WHERE tbl_name=$name AND type IN ('index','trigger') AND sql IS NOT NULL;", token, new() { ["$name"] = table }))[0].Rows;
            var hasSequence = (await RunAsync(connection, "SELECT count(*) FROM sqlite_schema WHERE name='sqlite_sequence';", token))[0].Rows[0][0];
            object? sequence = null;
            if (Convert.ToInt64(hasSequence) > 0)
                sequence = (await RunAsync(connection, "SELECT seq FROM sqlite_sequence WHERE name=$name;", token, new() { ["$name"] = table }))[0].Rows.FirstOrDefault()?[0];
            var temporary = "__wb_rebuild_" + Guid.NewGuid().ToString("N");
            var suffix = schema.Sql[(schema.Sql.LastIndexOf(')') + 1)..];
            await RunAsync(connection, $"CREATE TABLE {Q(temporary)} ({string.Join(",", parts)}){suffix};", token);
            var targetSchema = await SchemaAsync(connection, temporary, token);
            var copy = string.Join(",", targetSchema.Columns.Where(c => c.Hidden == 0 && schema.Columns.Any(source => source.Name == c.Name)).Select(c => Q(c.Name)));
            // 保留没有显式 INTEGER PRIMARY KEY 的表的 rowid，避免已有行定位变化。
            var sourceCopy = copy;
            if (schema.RowId is not null && targetSchema.RowId is not null)
            { copy = Q(targetSchema.RowId) + "," + copy; sourceCopy = Q(schema.RowId) + "," + sourceCopy; }
            await RunAsync(connection, $"INSERT INTO {Q(temporary)} ({copy}) SELECT {sourceCopy} FROM {Q(table)};", token);
            await RunAsync(connection, $"DROP TABLE {Q(table)};", token);
            await RunAsync(connection, "PRAGMA legacy_alter_table=ON;", token);
            await RunAsync(connection, $"ALTER TABLE {Q(temporary)} RENAME TO {Q(table)};", token);
            if (sequence is not null)
                await RunAsync(connection, "UPDATE sqlite_sequence SET seq=max(seq,$seq) WHERE name=$name;", token, new() { ["$name"] = table, ["$seq"] = sequence });
            foreach (var dependency in dependencies) await RunAsync(connection, (string)dependency[0]!, token);
            await SetColumnIndexAsync(connection, table, request, token);
            var violations = (await RunAsync(connection, "PRAGMA foreign_key_check;", token))[0];
            if (violations.Rows.Count > 0) throw new ArgumentException("修改导致外键约束无效，已回滚。");
        }, token);
    }

    private static string Definition(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4000) throw new ArgumentException("字段定义不能为空，最多 4000 字符。");
        // 表单定义仅容许单个完整字段片段；任意批处理请使用 SQL 控制台。
        if (SplitDefinitions("CREATE TABLE x (x " + value + ")").Count != 1 || value.Contains(';') || value.Contains("--") || value.Contains("/*"))
            throw new ArgumentException("请填写单个字段的类型和约束，例如 TEXT NOT NULL DEFAULT ''。");
        return value.Trim();
    }

    // 只在顶层逗号处分段，保留 CHECK、复合约束及引号内的标点。
    internal static List<string> SplitDefinitions(string sql)
    {
        var start = -1;
        char quote = '\0';
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (SkipQuotedOrComment(sql, ref i, ref quote)) continue;
            if (c == '(') { start = i; break; }
        }
        if (start < 0) throw new ArgumentException("无法解析建表 SQL。");
        var parts = new List<string>();
        var depth = 1;
        quote = '\0';
        var segment = start + 1;
        for (var i = segment; i < sql.Length; i++)
        {
            var c = sql[i];
            if (SkipQuotedOrComment(sql, ref i, ref quote)) continue;
            if (c == '(') depth++;
            if (c == ')') depth--;
            if (c == ')' && depth == 0)
            {
                parts.Add(sql[segment..i].Trim());
                var suffix = sql[(i + 1)..].Trim().TrimEnd(';').Trim();
                if (suffix.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(suffix, @"^(?:STRICT|WITHOUT\s+ROWID)(?:\s*,\s*(?:STRICT|WITHOUT\s+ROWID))*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    throw new ArgumentException("无法解析表选项，请使用 SQL 控制台。");
                return parts;
            }
            if (c == ',' && depth == 1) { parts.Add(sql[segment..i].Trim()); segment = i + 1; }
        }
        throw new ArgumentException("字段定义引号或括号不匹配。");
    }

    private static bool SkipQuotedOrComment(string sql, ref int i, ref char quote)
    {
        var c = sql[i];
        if (quote != '\0')
        {
            if (c == quote) { if (i + 1 < sql.Length && sql[i + 1] == quote) i++; else quote = '\0'; }
            return true;
        }
        if (c is '\'' or '"' or '`' or '[') { quote = c == '[' ? ']' : c; return true; }
        if (i + 1 < sql.Length && c == '-' && sql[i + 1] == '-')
        { while (i + 1 < sql.Length && sql[i + 1] != '\n') i++; return true; }
        if (i + 1 < sql.Length && c == '/' && sql[i + 1] == '*')
        {
            var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
            if (end < 0) throw new ArgumentException("SQL 注释未结束。");
            i = end + 1; return true;
        }
        return false;
    }

    private static (string Name, int End) ReadName(string definition)
    {
        if (definition.Length == 0) return ("", 0);
        if (definition[0] is '"' or '`' or '[' or '\'')
        {
            var close = definition[0] == '[' ? ']' : definition[0];
            var name = "";
            for (var i = 1; i < definition.Length; i++)
            {
                if (definition[i] != close) { name += definition[i]; continue; }
                if (i + 1 < definition.Length && definition[i + 1] == close) { name += close; i++; continue; }
                return (name, i + 1);
            }
        }
        var end = 0;
        while (end < definition.Length && !char.IsWhiteSpace(definition[end])) end++;
        return (definition[..end], end);
    }

    public async Task RowAsync(string table, DatabaseRowRequest request, string action, CancellationToken token)
    {
        operation = action + "-row";
        await using var connection = await OpenAsync(token);
        await AtomicAsync(connection, async () =>
        {
            var schema = await SchemaAsync(connection, table, token);
            if (schema.View) throw new ArgumentException("视图请使用 SQL 控制台操作。");
            var parameters = new Dictionary<string, object?>();
            foreach (var name in request.Values.Keys)
                if (!schema.Columns.Any(c => c.Name == name && c.Hidden == 0)) throw new ArgumentException("字段不存在或不可写：" + name);
            var values = request.Values.Select((pair, i) => { var p = "$v" + i; parameters[p] = Decode(pair.Value); return (pair.Key, Parameter: p); }).ToArray();
            string sql;
            if (action == "insert") sql = values.Length == 0 ? $"INSERT INTO {Q(table)} DEFAULT VALUES;" : $"INSERT INTO {Q(table)} ({string.Join(",", values.Select(v => Q(v.Key)))}) VALUES ({string.Join(",", values.Select(v => v.Parameter))});";
            else
            {
                if (request.Original is null || request.Original.Count == 0) throw new ArgumentException("修改和删除必须携带原始行快照。");
                var predicates = new List<string>();
                if (schema.RowId is not null && request.RowId is not null)
                { parameters["$rowid"] = long.Parse(request.RowId, System.Globalization.CultureInfo.InvariantCulture); predicates.Add(Q(schema.RowId) + "=$rowid"); }
                else if (!schema.Columns.Where(c => c.PrimaryKey > 0).Any() || schema.Columns.Where(c => c.PrimaryKey > 0).Any(c => !request.Original.TryGetValue(c.Name, out var key) || key.ValueKind == JsonValueKind.Null))
                    throw new ArgumentException("无法唯一定位此行，请使用 SQL 控制台。");
                var originalColumns = schema.Columns.Where(c => c.Hidden != 1).ToArray();
                if (originalColumns.Any(c => !request.Original.ContainsKey(c.Name))) throw new ArgumentException("原始行快照不完整，请刷新。");
                foreach (var c in originalColumns)
                {
                    var p = "$o" + parameters.Count; parameters[p] = Decode(request.Original[c.Name]); predicates.Add(Q(c.Name) + " COLLATE BINARY IS " + p);
                }
                if (action != "delete" && values.Length == 0) throw new ArgumentException("请至少填写一个待更新字段。");
                sql = action == "delete" ? $"DELETE FROM {Q(table)} WHERE {string.Join(" AND ", predicates)};" : $"UPDATE {Q(table)} SET {string.Join(",", values.Select(v => Q(v.Key) + "=" + v.Parameter))} WHERE {string.Join(" AND ", predicates)};";
            }
            await RunAsync(connection, sql, token, parameters);
            var changes = (await RunAsync(connection, "SELECT changes();", token))[0].Rows[0][0];
            if (action != "insert" && Convert.ToInt64(changes) != 1) throw new DatabaseRowConflictException("数据已被其他操作修改或无法唯一定位，请刷新后重试。当前操作已回滚。");
        }, token);
    }
}

public sealed class DatabaseRowConflictException(string message) : Exception(message);
