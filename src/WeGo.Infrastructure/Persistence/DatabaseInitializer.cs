using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace WeGo.Infrastructure.Persistence;

/// <summary>
/// Brings the SQLite file up to date at startup: applies migrations, then turns
/// on WAL. WAL is a property of the database file (it persists across opens),
/// so unlike the per-connection PRAGMAs it only needs setting once.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        WeGoDbContext context,
        DatabaseOptions options,
        CancellationToken cancellationToken = default)
    {
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        if (!options.EnableWal)
        {
            return;
        }

        var connection = context.Database.GetDbConnection();
        if (connection is not SqliteConnection)
        {
            return;
        }

        await context.Database
            .ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Normalises a connection string so the busy timeout is honoured even
    /// before the first PRAGMA runs, and so an in-memory or relative path still
    /// resolves the same way in every host.
    /// </summary>
    public static string BuildConnectionString(DatabaseOptions options)
    {
        var builder = new SqliteConnectionStringBuilder(options.ConnectionString)
        {
            ForeignKeys = true,
            // SqliteConnection exposes the busy timeout in whole seconds here;
            // the exact millisecond value is applied by the PRAGMA interceptor.
            DefaultTimeout = Math.Max(1, (int)Math.Ceiling(options.BusyTimeoutMs / 1000.0)),
        };

        return builder.ToString();
    }
}
