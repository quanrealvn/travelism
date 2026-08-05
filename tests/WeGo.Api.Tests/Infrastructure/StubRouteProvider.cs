using WeGo.Infrastructure.Routing;

namespace WeGo.Api.Tests.Infrastructure;

/// <summary>
/// Stands in for OSRM. Returning null models every failure the spec cares
/// about at once — timeout, 500, and a 200 with no route (§7.5) — because the
/// provider collapses all three to the same answer.
/// </summary>
public sealed class StubRouteProvider : IRouteProvider
{
    private int _calls;

    /// <summary>What every lookup returns. Null means "fall back to haversine".</summary>
    public RouteResult? Result { get; set; } = new(25, 12_000);

    public int Calls => Volatile.Read(ref _calls);

    public Task<RouteResult?> GetDrivingRouteAsync(
        double fromLat,
        double fromLng,
        double toLat,
        double toLng,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Result);
    }
}
