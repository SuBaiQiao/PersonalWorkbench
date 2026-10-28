using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using PersonalWorkbench.Controllers;
using PersonalWorkbench.Data;
using PersonalWorkbench.Services;

var root = Path.GetFullPath(Path.Combine("artifacts", "database-checks-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root, WebRootPath = Path.GetFullPath("PersonalWorkbenchWeb/dist") });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddDbContext<WorkbenchDbContext>(options => options.UseSqlite($"Data Source={Path.Combine(root, "business.db")};Pooling=False"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<DatabaseAuditService>();
builder.Services.AddScoped<DatabaseManagementService>();
builder.Services.AddScoped<DatabaseTransferService>();
builder.Services.AddScoped<ToolCatalogService>();
builder.Services.AddControllers().AddApplicationPart(typeof(DatabaseManagementController).Assembly);
await using var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WorkbenchDbContext>();
    await db.Database.EnsureCreatedAsync();
    await MenuDatabaseInitializer.InitializeAsync(db);
}
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");
await app.StartAsync();
using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(90) };
http.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
const string endpoint = "/api/database/manage";
var checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception("FAILED: " + label); Console.WriteLine("PASS " + label); checks++; }
async Task<JsonElement> Send(HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
{
    using var request = new HttpRequestMessage(method, endpoint + path) { Content = body is null ? null : JsonContent.Create(body) };
    using var response = await http.SendAsync(request);
    var content = await response.Content.ReadAsStringAsync();
    if (response.StatusCode != expected) throw new Exception($"{method} {path} expected {expected}, got {response.StatusCode}: {content}");
    return JsonSerializer.Deserialize<JsonElement>(content);
}
Task<JsonElement> Sql(string sql, HttpStatusCode expected = HttpStatusCode.OK) => Send(HttpMethod.Post, "/sql", new { sql }, expected);
Task<JsonElement> Rows(string name, string extra = "") => Send(HttpMethod.Get, "/rows?table=" + Uri.EscapeDataString(name) + extra);
Task<JsonElement> Structure(string name) => Send(HttpMethod.Get, "/structure?table=" + Uri.EscapeDataString(name));
Task<JsonElement> Post(string path, object? body = null) => Send(HttpMethod.Post, path, body);
Task<JsonElement> Put(string path, object body) => Send(HttpMethod.Put, path, body);
Task<JsonElement> Delete(string path) => Send(HttpMethod.Delete, path);

var tables = await Send(HttpMethod.Get, "/tables");
Check(tables.EnumerateArray().Any(t => t.GetProperty("name").GetString() == "tool_menus"), "现有 SQLite 业务表可浏览");
Check(!tables.EnumerateArray().Any(t => t.GetProperty("name").GetString() == "sql_logs"), "审计库独立于业务库");
await Post("/tables", new { name = "sample", columns = new[] { new { name = "id", definition = "INTEGER PRIMARY KEY AUTOINCREMENT" }, new { name = "name", definition = "TEXT NOT NULL DEFAULT ''" }, new { name = "payload", definition = "BLOB" }, new { name = "amount", definition = "INTEGER" } } });
Check((await Structure("sample")).GetProperty("columns").GetArrayLength() == 4, "创建表和结构查询");
await Post("/rows?table=sample", new { values = new { name = "中文' ; DROP TABLE sample; --", payload = new Dictionary<string, string> { ["$type"] = "blob", ["base64"] = "AP8=" }, amount = new Dictionary<string, string> { ["$type"] = "integer", ["value"] = "9223372036854775807" } } });
var row = (await Rows("sample")).GetProperty("rows")[0];
Check(row.GetProperty("values").GetProperty("name").GetString()!.Contains("DROP TABLE"), "绑定参数阻止数据内容变成 SQL");
Check(row.GetProperty("values").GetProperty("payload").GetProperty("base64").GetString() == "AP8=" && row.GetProperty("values").GetProperty("amount").GetProperty("value").GetString() == "9223372036854775807", "BLOB 与 64 位整数无损往返");
await Put("/row?table=sample", new { values = new { name = "已编辑", payload = (string?)null }, original = row.GetProperty("values"), rowId = row.GetProperty("rowId").GetString() });
await Send(HttpMethod.Put, "/row?table=sample", new { values = new { name = "过期修改" }, original = row.GetProperty("values"), rowId = row.GetProperty("rowId").GetString() }, HttpStatusCode.Conflict);
Check((await Rows("sample")).GetProperty("rows")[0].GetProperty("values").GetProperty("name").GetString() == "已编辑", "并发修改冲突返回 409，不覆盖新数据");
await Post("/rows?table=sample", new { values = new { name = "other" } });
Check((await Rows("sample", "&search=已编辑&pageSize=1")).GetProperty("total").GetInt64() == 1, "数据搜索与分页");
await Post("/columns?table=sample", new { name = "extra", definition = "TEXT DEFAULT 'a,b()'" });
Check((await Structure("sample")).GetProperty("columns").GetArrayLength() == 5, "新增带默认值的字段");
await Delete("/column?table=sample&column=extra");
await Sql("CREATE INDEX idx_sample_name ON sample(name); CREATE TABLE events(value TEXT); CREATE TRIGGER trg_sample AFTER UPDATE ON sample BEGIN INSERT INTO events VALUES(new.name); END; CREATE VIEW sample_view AS SELECT name FROM sample; CREATE TABLE child(parent INTEGER REFERENCES sample(id)); INSERT INTO child VALUES(1); INSERT INTO sample(id,name) VALUES(500,'deleted'); DELETE FROM sample WHERE id=500;");
await Put("/column?table=sample&column=name", new { name = "title", definition = "TEXT NOT NULL DEFAULT '' CHECK(length(title)<200)" });
var rebuilt = await Structure("sample");
Check(rebuilt.GetProperty("columns").EnumerateArray().Any(c => c.GetProperty("name").GetString() == "title"), "字段重命名及类型约束重建");
var dependencies = await Sql("SELECT name FROM sqlite_schema WHERE name IN ('idx_sample_name','trg_sample','sample_view'); SELECT * FROM sample_view; PRAGMA foreign_key_check;");
Check(dependencies.GetProperty("results")[0].GetProperty("rows").GetArrayLength() == 3, "重建保留索引、触发器和视图");
await Post("/rows?table=sample", new { values = new { title = "sequence" } });
Check((await Rows("sample", "&search=sequence")).GetProperty("rows")[0].GetProperty("values").GetProperty("id").GetInt64() == 501, "重建保留 AUTOINCREMENT 历史高水位");
var recent = (await Rows("sample")).GetProperty("rows")[0];
await Put("/row?table=sample", new { values = new { title = "trigger-check" }, original = recent.GetProperty("values"), rowId = recent.GetProperty("rowId").GetString() });
Check((await Rows("events")).GetProperty("rows")[0].GetProperty("values").GetProperty("value").GetString() == "trigger-check", "重建后触发器仍正常运行");
await Send(HttpMethod.Put, "/column?table=sample&column=title", new { name = "bad_title", definition = "TEXT NOT NULL CHECK(length(bad_title)<1)" }, HttpStatusCode.BadRequest);
Check((await Structure("sample")).GetProperty("columns").EnumerateArray().Any(c => c.GetProperty("name").GetString() == "title"), "字段重建失败回滚名称、结构和数据");
await Sql("INSERT INTO events VALUES('rollback-me'); SELECT missing_column FROM events;", HttpStatusCode.BadRequest);
Check((await Rows("events", "&search=rollback-me")).GetProperty("total").GetInt64() == 0, "多语句 SQL 失败整体回滚");
await Sql("INSERT INTO events VALUES('early-commit'); COMMIT;", HttpStatusCode.BadRequest);
Check((await Rows("events", "&search=early-commit")).GetProperty("total").GetInt64() == 0, "拒绝脚本提前提交事务");
await Sql("ATTACH DATABASE ':memory:' AS other;", HttpStatusCode.BadRequest);
Check(true, "管理连接拒绝 ATTACH，无法挂载日志库");
var multi = await Sql("SELECT 1 AS same,2 AS same; SELECT 3 AS next;");
Check(multi.GetProperty("results").GetArrayLength() == 2 && multi.GetProperty("results")[0].GetProperty("columns")[1].GetString() == "same", "多结果集与重复列名不丢失");
var capped = await Sql("WITH RECURSIVE x(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM x WHERE n<1010) SELECT n FROM x;");
Check(capped.GetProperty("results")[0].GetProperty("rows").GetArrayLength() == 1000 && capped.GetProperty("results")[0].GetProperty("truncated").GetBoolean(), "SQL 结果限制并明确标识截断");
await Sql("CREATE TABLE composite (a TEXT,b INTEGER,value TEXT,PRIMARY KEY(a,b)) WITHOUT ROWID; INSERT INTO composite VALUES('a',1,'before');");
var composite = (await Rows("composite")).GetProperty("rows")[0];
await Put("/row?table=composite", new { values = new { value = "after" }, original = composite.GetProperty("values") });
Check((await Rows("composite")).GetProperty("rows")[0].GetProperty("values").GetProperty("value").GetString() == "after", "WITHOUT ROWID 表通过复合主键编辑");
await Put("/column?table=composite&column=value", new { name = "value", definition = "TEXT NOT NULL" });
Check((await Rows("composite")).GetProperty("total").GetInt64() == 1, "重建保留复合主键和 WITHOUT ROWID");
await Sql("CREATE TABLE \"quote(\" (\"a,b\" TEXT DEFAULT 'x,)'); INSERT INTO \"quote(\" DEFAULT VALUES;");
Check((await Structure("quote(")).GetProperty("columns")[0].GetProperty("definition").GetString()!.Contains("x,)"), "解析引号名称及默认值中的括号逗号");
await Put("/column?table=" + Uri.EscapeDataString("quote(") + "&column=" + Uri.EscapeDataString("a,b"), new { name = "a,b", definition = "TEXT NOT NULL DEFAULT 'x,)'" });
Check((await Rows("quote(")).GetProperty("total").GetInt64() == 1, "特殊名称字段重建无损");
await Sql("CREATE TABLE bigrow(value TEXT); INSERT INTO bigrow(rowid,value) VALUES(9223372036854775807,'big');");
var bigrow = (await Rows("bigrow")).GetProperty("rows")[0];
Check(bigrow.GetProperty("rowId").GetString() == "9223372036854775807", "最大 rowid 以字符串返回不丢精度");
await Post("/delete-row?table=bigrow", new { values = new { }, original = bigrow.GetProperty("values"), rowId = bigrow.GetProperty("rowId").GetString() });
Check((await Rows("bigrow")).GetProperty("total").GetInt64() == 0, "删除大 rowid 数据");
await Put("/table?table=bigrow", new { name = "renamed" });
await Post("/rows?table=renamed", new { values = new { value = "test" } });
await Post("/clear?table=renamed");
Check((await Rows("renamed")).GetProperty("total").GetInt64() == 0, "修改表名与清空表");
await Delete("/table?table=renamed");
Check(!(await Send(HttpMethod.Get, "/tables")).EnumerateArray().Any(t => t.GetProperty("name").GetString() == "renamed"), "删除表");
var failedLogs = await Send(HttpMethod.Get, "/logs?status=failed&search=missing_column");
Check(failedLogs.GetProperty("total").GetInt64() > 0 && failedLogs.GetProperty("items")[0].GetProperty("error").GetString()!.Contains("missing_column"), "失败 SQL 和错误持久化留存");
var parameterLogs = await Send(HttpMethod.Get, "/logs?search=9223372036854775807");
Check(parameterLogs.GetProperty("total").GetInt64() > 0, "绑定参数包含在可搜索日志中");
Check((await Send(HttpMethod.Get, "/logs?search=" + Uri.EscapeDataString("已编辑"))).GetProperty("total").GetInt64() > 0, "中文绑定参数可直接检索");
await Sql("CREATE TABLE generated (id INTEGER PRIMARY KEY,value INTEGER,doubled INTEGER GENERATED ALWAYS AS (value*2) STORED) STRICT; INSERT INTO generated VALUES(1,3);");
await Put("/column?table=generated&column=doubled", new { name = "doubled", definition = "INTEGER" });
Check((await Rows("generated")).GetProperty("rows")[0].GetProperty("values").GetProperty("doubled").GetInt64() == 6, "生成字段改为普通字段时保留值及 STRICT 选项");
await Put("/column?table=generated&column=doubled", new { name = "doubled", definition = "INTEGER GENERATED ALWAYS AS (value*3) STORED" });
Check((await Rows("generated")).GetProperty("rows")[0].GetProperty("values").GetProperty("doubled").GetInt64() == 9, "普通字段改为生成字段时重新计算值");
var requestId = (await Send(HttpMethod.Get, "/logs?search=SELECT&status=success")).GetProperty("items")[0].GetProperty("request_id").GetString();
Check((await Send(HttpMethod.Get, "/logs?search=" + Uri.EscapeDataString(requestId!))).GetProperty("total").GetInt64() > 1, "通过请求 ID 关联事务内的 SQL");
await Post("/tables", new { name = "selectable", columns = new[] {
    new { name = "id", definition = "INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL", index = "none" },
    new { name = "name", definition = "TEXT NOT NULL", index = "unique" },
    new { name = "tag", definition = "TEXT", index = "index" }
} });
Check((await Structure("selectable")).GetProperty("indexes").GetArrayLength() == 2, "建表时按选择创建普通和唯一索引");
await Post("/rows?table=selectable", new { values = new { name = "one", tag = "a" } });
await Send(HttpMethod.Post, "/rows?table=selectable", new { values = new { name = "one" } }, HttpStatusCode.BadRequest);
Check((await Rows("selectable")).GetProperty("total").GetInt64() == 1, "选择的唯一索引实际限制重复数据");
await Post("/indexes?table=selectable", new { name = "idx_selectable_combo", columns = new[] { "tag", "name" }, unique = false });
var combo = (await Structure("selectable")).GetProperty("indexes").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "idx_selectable_combo");
Check(combo.GetProperty("columns")[0].GetString() == "tag" && combo.GetProperty("columns")[1].GetString() == "name", "多字段索引保持选择顺序");
await Put("/column?table=selectable&column=name", new { name = "name", definition = "TEXT NOT NULL", index = "none" });
var remainingIndexes = (await Structure("selectable")).GetProperty("indexes");
Check(remainingIndexes.GetArrayLength() == 2 && remainingIndexes.EnumerateArray().Any(i => i.GetProperty("name").GetString() == "idx_selectable_combo"), "取消单字段索引保留复合索引及其他字段索引");
await Post("/rows?table=selectable", new { values = new { name = "one", tag = "b" } });
await Send(HttpMethod.Put, "/column?table=selectable&column=name", new { name = "renamed_name", definition = "TEXT NOT NULL", index = "unique" }, HttpStatusCode.BadRequest);
Check((await Structure("selectable")).GetProperty("columns").EnumerateArray().Any(c => c.GetProperty("name").GetString() == "name") && (await Rows("selectable")).GetProperty("total").GetInt64() == 2, "唯一索引创建失败回滚字段重命名及全部数据");
await Delete("/index?table=selectable&name=idx_selectable_combo");
Check(!(await Structure("selectable")).GetProperty("indexes").EnumerateArray().Any(i => i.GetProperty("name").GetString() == "idx_selectable_combo"), "可视化索引删除接口正常");
await Sql("CREATE TABLE protected_index(value TEXT UNIQUE); CREATE TABLE add_primary(value TEXT); INSERT INTO add_primary VALUES('a'),('b');");
var automaticIndex = (await Structure("protected_index")).GetProperty("indexes")[0].GetProperty("name").GetString();
await Send(HttpMethod.Delete, "/index?table=protected_index&name=" + Uri.EscapeDataString(automaticIndex!), expected: HttpStatusCode.BadRequest);
Check(true, "约束生成的自动索引禁止直接删除");
await Post("/columns?table=add_primary", new { name = "id", definition = "INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL", index = "none" });
var addedPrimaryRows = (await Rows("add_primary")).GetProperty("rows");
Check(addedPrimaryRows.GetArrayLength() == 2 && addedPrimaryRows[0].GetProperty("values").GetProperty("id").GetInt64() == 1, "已有数据的表新增自增主键并保留原有行");
await Post("/columns?table=add_primary", new { name = "category", definition = "TEXT NOT NULL DEFAULT 'default'", index = "index" });
Check((await Rows("add_primary")).GetProperty("rows")[0].GetProperty("values").GetProperty("category").GetString() == "default" && (await Structure("add_primary")).GetProperty("indexes").GetArrayLength() == 1, "新增非空字段应用默认值并创建索引");
Check((await Send(HttpMethod.Get, "/logs?search=create-index")).GetProperty("total").GetInt64() > 0, "可视化索引操作完整记录 SQL 日志");
var beforeImport = (await Send(HttpMethod.Get, "/logs")).GetProperty("total").GetInt64();
using (var scope = app.Services.CreateScope())
{
    var transfer = scope.ServiceProvider.GetRequiredService<DatabaseTransferService>();
    var backup = await transfer.CreateBackupAsync(default);
    await using (var stream = File.OpenRead(backup)) await transfer.RestoreDatabaseAsync(stream, default);
}
var reloadedAudit = new DatabaseAuditService(app.Environment);
var reloaded = JsonSerializer.SerializeToElement(await reloadedAudit.ListAsync(1, 20, null, null));
Check(reloaded.GetProperty("total").GetInt64() == beforeImport, "日志独立持久化，重新创建服务和整库导入后仍保留");
http.DefaultRequestHeaders.Remove("X-Requested-With");
await Send(HttpMethod.Post, "/sql", new { sql = "DELETE FROM sample;" }, HttpStatusCode.Forbidden);
Check(true, "修改 API 校验请求头");
Console.WriteLine($"数据库管理验证通过：{checks} 项。独立目录：{root}");
if (args.Contains("--serve")) { Console.WriteLine("UI 验证地址：" + http.BaseAddress + "settings/database"); await app.WaitForShutdownAsync(); }
else await app.StopAsync();
