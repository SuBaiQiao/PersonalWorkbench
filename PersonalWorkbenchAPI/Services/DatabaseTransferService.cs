using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PersonalWorkbench.Data;

namespace PersonalWorkbench.Services;

public sealed class DatabaseTransferService(WorkbenchDbContext db)
{
    private static readonly SemaphoreSlim TransferLock = new(1, 1);

    public async Task<string> CreateBackupAsync(CancellationToken cancellationToken)
    {
        await TransferLock.WaitAsync(cancellationToken);
        try
        {
            var source = (SqliteConnection)db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using (var checkpoint = source.CreateCommand())
                {
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(FULL);";
                    await checkpoint.ExecuteNonQueryAsync(cancellationToken);
                }

                var backupPath = Path.Combine(Path.GetTempPath(), $"personal-workbench-{Guid.NewGuid():N}.db");
                await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath }.ToString()))
                {
                    await destination.OpenAsync(cancellationToken);
                    source.BackupDatabase(destination);
                }

                return backupPath;
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
                SqliteConnection.ClearAllPools();
            }
        }
        finally
        {
            TransferLock.Release();
        }
    }

    public async Task<int> RestoreDatabaseAsync(Stream upload, CancellationToken cancellationToken)
    {
        var importPath = Path.Combine(Path.GetTempPath(), $"personal-workbench-import-{Guid.NewGuid():N}.db");
        try
        {
            await using (var file = File.Create(importPath))
            {
                await upload.CopyToAsync(file, cancellationToken);
            }

            var tableCount = await ValidateBackupAsync(importPath, cancellationToken);
            await TransferLock.WaitAsync(cancellationToken);
            try
            {
                await ReplaceDatabaseAsync(importPath, cancellationToken);
                return tableCount;
            }
            finally
            {
                TransferLock.Release();
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(importPath))
            {
                File.Delete(importPath);
            }
        }
    }

    private async Task<int> ValidateBackupAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await connection.OpenAsync(cancellationToken);

        await using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = await integrity.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(result as string, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("SQLite 文件未通过完整性检查。");
            }
        }

        await using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await schema.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        var requiredTables = db.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requiredTables.Any(table => !tables.Contains(table!)))
        {
            throw new InvalidDataException("数据库文件缺少当前应用需要的数据表。");
        }

        return tables.Count;
    }

    private async Task ReplaceDatabaseAsync(string importPath, CancellationToken cancellationToken)
    {
        var currentConnectionString = db.Database.GetDbConnection().ConnectionString;
        await db.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools();

        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = importPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await using var destination = new SqliteConnection(currentConnectionString);
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
        await destination.CloseAsync();
        await source.CloseAsync();
        SqliteConnection.ClearAllPools();
    }
}
