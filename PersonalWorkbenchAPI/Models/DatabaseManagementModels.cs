using System.Text.Json;

namespace PersonalWorkbench.Models;

public sealed record DatabaseSqlRequest(string Sql);
public sealed record DatabaseTableRequest(string Name, List<DatabaseColumnRequest> Columns);
public sealed record DatabaseColumnRequest(string Name, string Definition, string? Index = null);
public sealed record DatabaseIndexRequest(string Name, List<string> Columns, bool Unique = false);
public sealed record DatabaseIndex(string Name, bool Unique, string Origin, bool Partial, string? Sql, List<string?> Columns);
public sealed record DatabaseRenameRequest(string Name);
public sealed record DatabaseRowRequest(Dictionary<string, JsonElement> Values, Dictionary<string, JsonElement>? Original = null, string? RowId = null);
public sealed record DatabaseColumn(int Cid, string Name, string Type, bool NotNull, string? DefaultValue, int PrimaryKey, int Hidden, string Definition);
public sealed record DatabaseResult(string[] Columns, List<object?[]> Rows, bool Truncated, int AffectedRows);
