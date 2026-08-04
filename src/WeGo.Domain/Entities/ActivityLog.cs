using WeGo.Domain.Common;

namespace WeGo.Domain.Entities;

/// <summary>Append-only audit trail. Written in the same transaction as the mutation it describes (spec §8).</summary>
public sealed class ActivityLog : Entity
{
    public Guid TripId { get; set; }

    public Guid MemberId { get; set; }

    public ActivityAction Action { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public required string SummaryText { get; set; }

    public DateTimeOffset At { get; set; }
}
