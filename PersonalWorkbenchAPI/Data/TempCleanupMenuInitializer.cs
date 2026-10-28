using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PersonalWorkbench.Data;

/// <summary>为已存在的菜单数据库补充新工具，只执行一次，保留之后的人工修改或删除。</summary>
public static class TempCleanupMenuInitializer
{
    public static async Task InitializeAsync(WorkbenchDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS workbench_feature_updates (Id TEXT NOT NULL PRIMARY KEY);
                INSERT INTO menu_categories (Id, Name, "Order")
                SELECT 'system', '设备与监控', 30
                WHERE NOT EXISTS (SELECT 1 FROM workbench_feature_updates WHERE Id = 'temp-cleanup-menu-v1')
                  AND NOT EXISTS (SELECT 1 FROM menu_categories WHERE Id = 'system');
                INSERT INTO tool_menus (Name, Route, ComponentKey, Category, Icon, Tone, Description, Keywords, Features, "Order", IsEnabled)
                SELECT '关机临时清理', '/tools/temp-cleanup', 'TempCleanupView', 'system', 'folder', 'teal',
                       '关机时自动尝试清理 TEMP，释放临时文件占用。',
                       'temp tmp 临时 文件 清理 关机 重启 shutdown 自动 cleanup 缓存',
                       '["TEMP 临时文件","关机自动清理","清理预览","清理记录"]', 230, 1
                WHERE NOT EXISTS (SELECT 1 FROM workbench_feature_updates WHERE Id = 'temp-cleanup-menu-v1')
                  AND NOT EXISTS (SELECT 1 FROM tool_menus WHERE Route = '/tools/temp-cleanup' COLLATE NOCASE OR ComponentKey = 'TempCleanupView');
                INSERT OR IGNORE INTO workbench_feature_updates (Id) VALUES ('temp-cleanup-menu-v1');
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
