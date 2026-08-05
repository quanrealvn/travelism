using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Errors;
using WeGo.Domain;
using WeGo.Domain.Abstractions;
using WeGo.Domain.Common;
using WeGo.Domain.Entities;
using WeGo.Infrastructure.Persistence;
using WeGo.Infrastructure.Weather;

namespace WeGo.Api.Services;

/// <summary>
/// Spec §5.5. Forecast for the trip, cached for three hours, serving stale data
/// rather than nothing when the upstream service is down.
/// </summary>
public sealed class WeatherService(
    WeGoDbContext db,
    IWeatherProvider provider,
    IMemoryCache cache,
    OpenMeteoOptions options,
    IClock clock)
{
    private sealed record CachedForecast(WeatherForecast Forecast, DateTimeOffset FetchedAt);

    /// <summary>
    /// Returns the forecast, or null when there is nothing to forecast for
    /// (spec §5.5: no coordinates, or a trip already in the past).
    /// </summary>
    public async Task<Result<WeatherResponse?>> GetAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var trip = await db.Trips
            .AsNoTracking()
            .FirstAsync(t => t.Id == tripId, cancellationToken)
            .ConfigureAwait(false);

        var origin = await FindOriginAsync(tripId, cancellationToken).ConfigureAwait(false);
        if (origin is null)
        {
            // Spec §5.5 forbids a hard-coded fallback location: a forecast for
            // somewhere the trip is not would be worse than none.
            return Result<WeatherResponse?>.Ok(null);
        }

        // Spec §7.12: a trip wholly in the past has no forecast, and asking
        // upstream for one would be a wasted call.
        //
        // "Today" is resolved in the TRIP's timezone, never the server's and
        // never the browser's: a trip in Asia/Bangkok ends when it is over
        // there, whoever happens to be looking and from where.
        var today = TodayInTripTimeZone(trip.TimeZoneId);
        if (trip.EndDate < today)
        {
            return Result<WeatherResponse?>.Ok(null);
        }

        var cacheKey = $"weather:{tripId}";
        var isFresh = cache.TryGetValue<CachedForecast>(cacheKey, out var cached)
                      && cached is not null
                      && clock.UtcNow - cached.FetchedAt < TimeSpan.FromHours(options.CacheHours);

        if (isFresh)
        {
            return Result<WeatherResponse?>.Ok(ToResponse(cached!.Forecast, stale: false));
        }

        // The forecast only covers from today onwards; past days are history.
        var from = trip.StartDate < today ? today : trip.StartDate;

        var fetched = await provider
            .GetDailyAsync(origin.Value.Lat, origin.Value.Lng, from, trip.EndDate, trip.TimeZoneId, cancellationToken)
            .ConfigureAwait(false);

        if (fetched is not null)
        {
            cache.Set(
                cacheKey,
                new CachedForecast(fetched, clock.UtcNow),
                // Held well past the freshness window so it is still available
                // to serve stale during an outage.
                TimeSpan.FromHours(Math.Max(options.CacheHours * 8, 24)));

            return Result<WeatherResponse?>.Ok(ToResponse(fetched, stale: false));
        }

        // Spec §5.5: stale beats nothing, but it must say so.
        if (cached is not null)
        {
            return Result<WeatherResponse?>.Ok(ToResponse(cached.Forecast, stale: true));
        }

        return new Failure(
            StatusCodes.Status502BadGateway,
            ErrorCodes.WeatherUnavailable,
            "The forecast service is unavailable and nothing has been cached for this trip yet.");
    }

    /// <summary>
    /// Spec §5.5: the centroid of confirmed places, else the first place, else
    /// nothing at all.
    /// </summary>
    private async Task<(double Lat, double Lng)?> FindOriginAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var confirmed = await db.Places
            .AsNoTracking()
            .Where(p => p.TripId == tripId && p.Status == PlaceStatus.Confirmed)
            .Select(p => new { p.Lat, p.Lng })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (confirmed.Count > 0)
        {
            return (confirmed.Average(p => p.Lat), confirmed.Average(p => p.Lng));
        }

        var first = await db.Places
            .AsNoTracking()
            .Where(p => p.TripId == tripId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new { p.Lat, p.Lng })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return first is null ? null : (first.Lat, first.Lng);
    }

    private DateOnly TodayInTripTimeZone(string timeZoneId)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            // The trip's zone was validated on write, so this is unreachable in
            // practice; UTC is the least surprising fallback if it ever is not.
            return DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        }
        catch (InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        }
    }

    private static WeatherResponse ToResponse(WeatherForecast forecast, bool stale) =>
        new(
            forecast.Lat,
            forecast.Lng,
            forecast.TimeZoneId,
            stale,
            forecast.Days
                .Select(d => new DailyWeatherResponse(
                    d.Date, d.MaxTempC, d.MinTempC, d.PrecipitationMm, d.WeatherCode))
                .ToList());
}
