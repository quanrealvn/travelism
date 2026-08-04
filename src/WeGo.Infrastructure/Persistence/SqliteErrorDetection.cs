using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace WeGo.Infrastructure.Persistence;

/// <summary>
/// Turns a provider-specific constraint violation into something the API layer
/// can answer with a 409 instead of a 500. Pre-checking uniqueness with a SELECT
/// narrows the window but cannot close it: two concurrent requests can both see
/// "available" and both insert, and only the index catches the loser.
/// </summary>
public static class SqliteErrorDetection
{
    private const int SqliteConstraint = 19;

    public static bool IsUniqueConstraintViolation(Exception exception, string? involvingColumn = null)
    {
        var sqlite = FindSqliteException(exception);
        if (sqlite is null || sqlite.SqliteErrorCode != SqliteConstraint)
        {
            return false;
        }

        // SQLite reports "UNIQUE constraint failed: Members.TripId, Members.DisplayName".
        if (!sqlite.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return involvingColumn is null
            || sqlite.Message.Contains(involvingColumn, StringComparison.OrdinalIgnoreCase);
    }

    private static SqliteException? FindSqliteException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqlite)
            {
                return sqlite;
            }

            if (current is DbUpdateException && current.InnerException is null)
            {
                return null;
            }
        }

        return null;
    }
}
