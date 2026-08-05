using Microsoft.EntityFrameworkCore;
using WeGo.Domain.Abstractions;
using WeGo.Domain.Entities;
using WeGo.Domain.Itinerary;
using WeGo.Infrastructure.Persistence;
using WeGo.Infrastructure.Routing;

namespace WeGo.Api.Services;

/// <summary>
/// Travel times between places, cache-first (spec §5.4).
/// <para>
/// The ordering here is deliberate and load-bearing: every cache row is read
/// first, then the routing calls happen with nothing open, and only then is
/// anything written. Spec §5.4 forbids calling OSRM inside a transaction, and a
/// slow external service holding a SQLite write lock would stall every other
/// writer on the trip.
/// </para>
/// </summary>
public sealed class TravelTimeService(WeGoDbContext db, IRouteProvider routes, IClock clock)
{
    /// <summary>
    /// Resolves the legs needed for one day. At most one lookup per distinct
    /// pair, and none at all for pairs already cached.
    /// </summary>
    public async Task<IReadOnlyDictionary<(Guid From, Guid To), TravelLeg>> GetLegsAsync(
        Guid tripId,
        IReadOnlyList<(Guid From, Guid To)> pairs,
        CancellationToken cancellationToken)
    {
        var wanted = pairs.Distinct().ToList();
        var result = new Dictionary<(Guid, Guid), TravelLeg>();

        if (wanted.Count == 0)
        {
            return result;
        }

        var fromIds = wanted.Select(p => p.From).ToHashSet();
        var toIds = wanted.Select(p => p.To).ToHashSet();

        var cached = await db.TravelTimeCaches
            .AsNoTracking()
            .Where(c => c.TripId == tripId
                        && c.Mode == TravelTimeMode.Driving
                        && fromIds.Contains(c.FromPlaceId)
                        && toIds.Contains(c.ToPlaceId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byPair = cached.ToDictionary(c => (c.FromPlaceId, c.ToPlaceId));

        var missing = new List<(Guid From, Guid To)>();
        foreach (var pair in wanted)
        {
            if (byPair.TryGetValue(pair, out var row))
            {
                result[pair] = new TravelLeg(row.Minutes, row.Source);
            }
            else
            {
                missing.Add(pair);
            }
        }

        if (missing.Count == 0)
        {
            return result;
        }

        var placeIds = missing.SelectMany(p => new[] { p.From, p.To }).ToHashSet();
        var coordinates = await db.Places
            .AsNoTracking()
            .Where(p => p.TripId == tripId && placeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Lat, p.Lng })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byPlace = coordinates.ToDictionary(p => p.Id, p => (p.Lat, p.Lng));

        // No database work happens inside this loop — see the class comment.
        var fetched = new List<TravelTimeCache>();
        var now = clock.UtcNow;

        foreach (var (from, to) in missing)
        {
            if (!byPlace.TryGetValue(from, out var origin) || !byPlace.TryGetValue(to, out var destination))
            {
                continue;
            }

            var route = await routes
                .GetDrivingRouteAsync(origin.Lat, origin.Lng, destination.Lat, destination.Lng, cancellationToken)
                .ConfigureAwait(false);

            var (minutes, metres, source) = route is not null
                ? (route.Minutes, route.Meters, TravelTimeSource.Osrm)
                : (TravelEstimate.MinutesBetween(origin.Lat, origin.Lng, destination.Lat, destination.Lng),
                   (int)Math.Round(WeGo.Domain.Places.Geo.DistanceKm(
                       origin.Lat, origin.Lng, destination.Lat, destination.Lng) * 1000),
                   TravelTimeSource.Haversine);

            result[(from, to)] = new TravelLeg(minutes, source);

            fetched.Add(new TravelTimeCache
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                FromPlaceId = from,
                ToPlaceId = to,
                Mode = TravelTimeMode.Driving,
                Minutes = minutes,
                Meters = metres,
                Source = source,
                FetchedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await PersistAsync(fetched, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task PersistAsync(List<TravelTimeCache> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        db.TravelTimeCaches.AddRange(rows);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (SqliteErrorDetection.IsUniqueConstraintViolation(ex))
        {
            // Another request cached the same leg while we were fetching it.
            // The answer is already correct; only the write is redundant, and a
            // feasibility read must not fail because a cache write raced.
            db.ChangeTracker.Clear();
        }
    }
}
