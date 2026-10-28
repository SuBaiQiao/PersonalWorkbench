using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PersonalWorkbench.Data;

/// <summary>兼容 EnsureCreated 创建的旧库；建表和空菜单初始化在同一事务内完成。</summary>
public static class MenuDatabaseInitializer
{
    public static async Task InitializeAsync(WorkbenchDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            // 非延迟事务在检查空表前获取写锁，多个实例同时启动时不会重复初始化。
            using var transaction = connection.BeginTransaction(deferred: false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = ReadResource("Scripts.MenuSchema.sql");
            await command.ExecuteNonQueryAsync(cancellationToken);
            command.CommandText = "SELECT COUNT(*) FROM tool_menus;";
            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            if (count == 0)
            {
                command.CommandText = ReadResource("Scripts.InitializeMenus.sql");
                command.Parameters.AddWithValue("$categories", ReadResource("Data.menu-categories.json"));
                command.Parameters.AddWithValue("$tools", ReadResource("Data.tools.json"));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(MenuDatabaseInitializer).Assembly.GetManifestResourceStream("PersonalWorkbench." + name)
            ?? throw new InvalidOperationException($"菜单初始化资源缺失：{name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
