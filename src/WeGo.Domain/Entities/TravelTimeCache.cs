using WeGo.Domain.Common;

namespace WeGo.Domain.Entities;

/// <summary>
/// Memoised A→B travel time. Invalidated when either endpoint's coordinates
/// change (spec §7.4) and hard-deleted when either place is deleted (spec §7.13).
/// </summary>
public sealed class TravelTimeCache : Entity
{
    public Guid TripId { get; set; }

    public Guid FromPlaceId { get; set; }

    public Guid ToPlaceId { get; set; }

    public TravelTimeMode Mode { get; set; } = TravelTimeMode.Driving;

    public int Minutes { get; set; }

    public int Meters { get; set; }

    public TravelTimeSource Source { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
