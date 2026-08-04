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

            return Result<IReadOnlyList<GeocodeResultResponse>>.Ok(
                results.Select(r => new GeocodeResultResponse(r.Name, r.DisplayName, r.Lat, r.Lng, r.Kind))
                       .ToList());
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
