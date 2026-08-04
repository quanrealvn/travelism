namespace WeGo.Infrastructure.Geocoding;

/// <summary>One candidate location for a typed place name.</summary>
/// <param name="Name">Short label, suitable for prefilling the place name.</param>
/// <param name="DisplayName">Full address, to tell near-identical names apart.</param>
/// <param name="Kind">Upstream classification, e.g. "restaurant" — may be null.</param>
public sealed record GeocodeSearchResult(
    string Name,
    string DisplayName,
    double Lat,
    double Lng,
    string? Kind);

/// <summary>Raised when the upstream geocoder could not answer.</summary>
public sealed class GeocodingUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public interface IGeocoder
{
    /// <summary>
    /// Looks up candidate locations for a free-text place name.
    /// </summary>
    /// <param name="near">
    /// Optional bias point. Results elsewhere are still returned, but nearby
    /// ones rank higher — without it, "quán ăn" matches the whole planet.
    /// </param>
    /// <exception cref="GeocodingUnavailableException">The upstream service failed.</exception>
    Task<IReadOnlyList<GeocodeSearchResult>> SearchAsync(
        string query,
        int limit,
        (double Lat, double Lng)? near,
        CancellationToken cancellationToken);
}
