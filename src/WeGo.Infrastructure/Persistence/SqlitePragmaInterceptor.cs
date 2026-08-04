using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace WeGo.Infrastructure.Persistence;

/// <summary>
/// Applies the per-connection PRAGMAs SQLite needs for correct concurrent
/// behaviour. These cannot be set once at startup: <c>busy_timeout</c> and
/// <c>foreign_keys</c> are connection-scoped, and connection pooling means a
/// request may well get a connection this process has not configured yet.
/// </summary>
public sealed class SqlitePragmaInterceptor(DatabaseOptions options) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        command.ExecuteNonQuery();
    }

    private string PragmaSql =>
        // busy_timeout makes a writer wait rather than fail fast with SQLITE_BUSY
        // when another connection holds the write lock (spec §7.9).
        $"PRAGMA busy_timeout = {options.BusyTimeoutMs}; PRAGMA foreign_keys = ON;";
}
