using WeGo.Domain.Places;

namespace WeGo.Domain.Itinerary;

/// <summary>
/// Spec §5.4's fallback: when the routing service cannot answer, estimate from
/// straight-line distance. Pure, so the arithmetic is testable without a network.
/// </summary>
public static class TravelEstimate
{
    /// <summary>Roads are not straight; this scales the great-circle distance up.</summary>
    public const double RoadFactor = 1.35;

    /// <summary>Average driving speed, km/h, for mountain roads around Mộc Châu.</summary>
    public const double AverageSpeedKmh = 32.0;

    /// <summary>
    /// Minutes to drive between two points, rounded up. Rounding up rather than
    /// to nearest keeps the estimate conservative: under-stating travel is what
    /// makes a plan quietly impossible.
    /// </summary>
    public static int MinutesBetween(double fromLat, double fromLng, double toLat, double toLng)
    {
        var straightLineKm = Geo.DistanceKm(fromLat, fromLng, toLat, toLng);
        var roadKm = straightLineKm * RoadFactor;
        var hours = roadKm / AverageSpeedKmh;

        return (int)Math.Ceiling(hours * 60);
    }
}
