namespace WeGo.Domain.Places;

/// <summary>
/// Great-circle geometry. Pure and dependency-free — the same haversine backs
/// the travel-time fallback in a later milestone (spec §5.4).
/// </summary>
public static class Geo
{
    /// <summary>Mean Earth radius, the standard value used by the haversine formula.</summary>
    public const double EarthRadiusKm = 6371.0088;

    /// <summary>
    /// Straight-line distance between two points, in kilometres. Ignores roads
    /// and terrain: it answers "how far away is this", not "how long to drive".
    /// </summary>
    public static double DistanceKm(double fromLat, double fromLng, double toLat, double toLng)
    {
        var deltaLat = ToRadians(toLat - fromLat);
        var deltaLng = ToRadians(toLng - fromLng);

        var a = (Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2))
                + (Math.Cos(ToRadians(fromLat))
                   * Math.Cos(ToRadians(toLat))
                   * Math.Sin(deltaLng / 2)
                   * Math.Sin(deltaLng / 2));

        // Atan2 rather than Asin: it stays accurate for antipodal points, where
        // rounding can push the argument of Asin just past 1.
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusKm * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
