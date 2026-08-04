using Microsoft.EntityFrameworkCore;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Errors;
using WeGo.Domain.Common;
using WeGo.Domain.Places;
using WeGo.Infrastructure.Geocoding;
using WeGo.Infrastructure.Persistence;

namespace WeGo.Api.Services;

public sealed class GeocodingService(WeGoDbContext db, IGeocoder geocoder)
{
    public async Task<Result<IReadOnlyList<GeocodeResultResponse>>> SearchAsync(
        Guid tripId,
        string? query,
        int? limit,
        CancellationToken cancellationToken)
    {
        var (validQuery, validation) = GeocodeQuery.Validate(query);
        if (!validation.IsValid || validQuery is null)
        {
            return Failure.Validation(validation, "The search query failed validation.");
        }

        var near = await FindBiasPointAsync(tripId, cancellationToken).ConfigureAwait(false);

        try
        {
            var results = await geocoder
                .SearchAsync(validQuery, GeocodeQuery.ClampLimit(limit), near, cancellationToken)
                .ConfigureAwait(false);

            return Result<IReadOnlyList<GeocodeResultResponse>>.Ok(Rank(results, near));
        }
        catch (GeocodingUnavailableException ex)
        {
            // A search outage must not look like a bug in the trip: the client
            // falls back to entering coordinates by hand.
            return new Failure(
                StatusCodes.Status502BadGateway,
                ErrorCodes.GeocodingUnavailable,
                ex.Message);
        }
    }

    /// <summary>
    /// Orders matches by how far they are from the trip, nearest first, and
    /// attaches the distance.
    /// <para>
    /// Nominatim ranks by its own notion of importance, which for a Vietnamese
    /// place name can put another continent first — "Tiểu khu 32" returns a
    /// street in Kaohsiung, "Hang Táu" one in Hong Kong. Both look entirely
    /// plausible in a list of names. Sorting by distance puts anything near the
    /// trip on top, and carrying the distance lets the client say how far away
    /// the rest are rather than presenting them as equals.
    /// </para>
    /// </summary>
    private static List<GeocodeResultResponse> Rank(
        IReadOnlyList<GeocodeSearchResult> results,
        (double Lat, double Lng)? near)
    {
        var mapped = results.Select(r => new GeocodeResultResponse(
            r.Name,
            r.DisplayName,
            r.Lat,
            r.Lng,
            r.Kind,
            near is { } point ? Geo.DistanceKm(point.Lat, point.Lng, r.Lat, r.Lng) : null));

        // With no places yet there is nothing to measure from, so the upstream
        // ordering is left exactly as it came.
        return near is null
            ? mapped.ToList()
            : mapped.OrderBy(r => r.DistanceKm).ToList();
    }

    /// <summary>
    /// Centroid of the places already on the trip, used to rank nearby matches
    /// higher. Null for an empty trip, where there is nothing to bias towards.
    /// </summary>
    private async Task<(double Lat, double Lng)?> FindBiasPointAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var coordinates = await db.Places
            .AsNoTracking()
            .Where(p => p.TripId == tripId)
            .Select(p => new { p.Lat, p.Lng })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (coordinates.Count == 0)
        {
            return null;
        }

        return (coordinates.Average(c => c.Lat), coordinates.Average(c => c.Lng));
    }
}
