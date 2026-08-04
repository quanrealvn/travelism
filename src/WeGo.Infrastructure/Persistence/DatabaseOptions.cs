namespace WeGo.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = "Data Source=wego.db";

    /// <summary>
    /// Spec §7.9 asks for <c>BusyTimeout=5000</c>. Microsoft.Data.Sqlite has no
    /// such connection-string keyword, so the value is carried here and applied
    /// two ways: as <c>Default Timeout</c> on the connection string and as
    /// <c>PRAGMA busy_timeout</c> on every connection open. See DECISIONS.md.
    /// </summary>
    public int BusyTimeoutMs { get; set; } = 5000;

    /// <summary>WAL lets readers run concurrently with the single writer.</summary>
    public bool EnableWal { get; set; } = true;
}
