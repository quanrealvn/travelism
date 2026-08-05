namespace WeGo.Infrastructure.Routing;

/// <summary>A driving route between two points.</summary>
public sealed record RouteResult(int Minutes, int Meters);

/// <summary>
/// Driving times from a routing service.
/// </summary>
public interface IRouteProvider
{
    /// <summary>
    /// The driving route between two points, or null when the service could not
    /// answer — timed out, failed, or found no road connecting them.
    /// <para>
    /// Null rather than an exception: spec §5.4 treats every one of those the
    /// same way, by falling back to a straight-line estimate. A route that does
    /// not exist is an ordinary answer, not a failure.
    /// </para>
    /// </summary>
    Task<RouteResult?> GetDrivingRouteAsync(
        double fromLat,
        double fromLng,
        double toLat,
        double toLng,
        CancellationToken cancellationToken);
}
