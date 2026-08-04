namespace WeGo.Domain.Common;

/// <summary>
/// Spec §3: every entity carries Id, CreatedAt, UpdatedAt, UpdatedByMemberId.
/// Timestamps are always UTC — <see cref="DateTimeOffset"/> is used rather than
/// <see cref="DateTime"/> so the offset survives the SQLite round-trip and no
/// code path can accidentally reinterpret a stored instant in local time.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedByMemberId { get; set; }
}
