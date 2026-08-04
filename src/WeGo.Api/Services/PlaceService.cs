using Microsoft.EntityFrameworkCore;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Errors;
using WeGo.Domain.Abstractions;
using WeGo.Domain.Common;
using WeGo.Domain.Entities;
using WeGo.Domain.Places;
using WeGo.Infrastructure.Persistence;

namespace WeGo.Api.Services;

public sealed class PlaceService(WeGoDbContext db, IClock clock, ActivityLogWriter activityLog)
{
    public async Task<IReadOnlyList<Place>> ListAsync(
        Guid tripId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        // The model-level filter hides soft-deleted rows everywhere by default;
        // this is the single opt-out the spec allows (§6), and the payload marks
        // each row with isDeleted so the client can tell them apart.
        var query = includeDeleted
            ? db.Places.IgnoreQueryFilters().Where(p => p.TripId == tripId)
            : db.Places.Where(p => p.TripId == tripId);

        return await query
            .AsNoTracking()
            .Include(p => p.Likes)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<Place>> GetAsync(Guid tripId, Guid placeId, CancellationToken cancellationToken)
    {
        var place = await db.Places
            .AsNoTracking()
            .Include(p => p.Likes)
            .FirstOrDefaultAsync(p => p.Id == placeId && p.TripId == tripId, cancellationToken)
            .ConfigureAwait(false);

        return place is null ? Failure.NotFound("Place not found.") : Result<Place>.Ok(place);
    }

    public async Task<Result<Place>> CreateAsync(
        Guid tripId,
        Guid actingMemberId,
        CreatePlaceRequest request,
        CancellationToken cancellationToken)
    {
        var (draft, validation) = PlaceRules.Validate(
            request.Name,
            request.Lat,
            request.Lng,
            request.Category,
            request.TimeSlots,
            request.EstimatedDurationMinutes,
            request.EstimatedCost,
            request.OpenHoursText);

        if (!validation.IsValid || draft is null)
        {
            return Failure.Validation(validation);
        }

        var now = clock.UtcNow;
        var place = new Place
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            Name = draft.Name,
            Lat = draft.Lat,
            Lng = draft.Lng,
            Category = draft.Category,
            TimeSlots = draft.TimeSlots,
            EstimatedDurationMinutes = draft.EstimatedDurationMinutes,
            EstimatedCost = draft.EstimatedCost,
            OpenHoursText = draft.OpenHoursText,
            // A new place starts as an unvetted Idea; only likes promote it
            // (spec §4). Recorded in DECISIONS.md — the spec does not name a
            // default explicitly.
            Status = PlaceStatus.Idea,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByMemberId = actingMemberId,
        };

        db.Places.Add(place);
        activityLog.Add(
            tripId,
            actingMemberId,
            ActivityAction.PlaceCreated,
            nameof(Place),
            place.Id,
            $"Added place “{place.Name}”.");

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<Place>.Ok(place);
    }

    public async Task<Result<Place>> UpdateAsync(
        Guid tripId,
        Guid placeId,
        Guid actingMemberId,
        UpdatePlaceRequest request,
        CancellationToken cancellationToken)
    {
        var place = await db.Places
            .Include(p => p.Likes)
            .FirstOrDefaultAsync(p => p.Id == placeId && p.TripId == tripId, cancellationToken)
            .ConfigureAwait(false);

        if (place is null)
        {
            return Failure.NotFound("Place not found.");
        }

        var currentSlots = request.TimeSlots.IsSet
            ? request.TimeSlots.Value
            : TimeSlotSet.ToNames(place.TimeSlots).Cast<string?>().ToArray();

        var (draft, validation) = PlaceRules.Validate(
            request.Name.Or(place.Name),
            request.Lat.IsSet ? request.Lat.Value : place.Lat,
            request.Lng.IsSet ? request.Lng.Value : place.Lng,
            request.Category.Or(place.Category.ToString()),
            currentSlots,
            request.EstimatedDurationMinutes.IsSet
                ? request.EstimatedDurationMinutes.Value
                : place.EstimatedDurationMinutes,
            request.EstimatedCost.IsSet ? request.EstimatedCost.Value : place.EstimatedCost,
            request.OpenHoursText.IsSet ? request.OpenHoursText.Value : place.OpenHoursText);

        if (!validation.IsValid || draft is null)
        {
            return Failure.Validation(validation);
        }

        // Spec §7.4: a place that moves invalidates every cached route touching
        // it, in both directions, inside this same transaction — otherwise
        // feasibility would keep quoting the travel time to where it used to be.
        if (PlaceRules.CoordinatesChanged(place, draft.Lat, draft.Lng))
        {
            await InvalidateTravelTimeCacheAsync(placeId, cancellationToken).ConfigureAwait(false);
        }

        place.Name = draft.Name;
        place.Lat = draft.Lat;
        place.Lng = draft.Lng;
        place.Category = draft.Category;
        place.TimeSlots = draft.TimeSlots;
        place.EstimatedDurationMinutes = draft.EstimatedDurationMinutes;
        place.EstimatedCost = draft.EstimatedCost;
        place.OpenHoursText = draft.OpenHoursText;
        place.UpdatedAt = clock.UtcNow;
        place.UpdatedByMemberId = actingMemberId;

        activityLog.Add(
            tripId,
            actingMemberId,
            ActivityAction.PlaceUpdated,
            nameof(Place),
            place.Id,
            $"Updated place “{place.Name}”.");

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<Place>.Ok(place);
    }

    /// <summary>
    /// Spec §5.6 / §7.13. A place is only ever soft-deleted, but its scheduled
    /// items and cached routes are removed for real: leaving them would let a
    /// deleted place keep influencing the itinerary and feasibility maths.
    /// Refusing without <c>force</c> is what stops that being a silent cascade.
    /// </summary>
    public async Task<Result<Place>> DeleteAsync(
        Guid tripId,
        Guid placeId,
        Guid actingMemberId,
        bool force,
        CancellationToken cancellationToken)
    {
        var place = await db.Places
            .Include(p => p.Likes)
            .FirstOrDefaultAsync(p => p.Id == placeId && p.TripId == tripId, cancellationToken)
            .ConfigureAwait(false);

        if (place is null)
        {
            return Failure.NotFound("Place not found.");
        }

        var scheduled = await db.ItineraryItems
            .Where(i => i.PlaceId == placeId && i.TripId == tripId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (scheduled.Count > 0 && !force)
        {
            var dates = scheduled
                .Select(i => i.Date)
                .Distinct()
                .OrderBy(d => d)
                .Select(d => d.ToString("yyyy-MM-dd"))
                .ToList();

            return Failure.Conflict(
                ErrorCodes.PlaceInUse,
                $"“{place.Name}” is scheduled on {dates.Count} day(s). "
                    + "Re-send with ?force=true to remove it from the itinerary as well.",
                new Dictionary<string, object?>
                {
                    ["dates"] = dates,
                    ["itineraryItemIds"] = scheduled.Select(i => i.Id).ToList(),
                });
        }

        if (scheduled.Count > 0)
        {
            db.ItineraryItems.RemoveRange(scheduled);
        }

        await InvalidateTravelTimeCacheAsync(placeId, cancellationToken).ConfigureAwait(false);

        place.IsDeleted = true;
        place.UpdatedAt = clock.UtcNow;
        place.UpdatedByMemberId = actingMemberId;

        // Spec §5.6: one entry summarising both effects, not one per row.
        var summary = scheduled.Count > 0
            ? $"Deleted place “{place.Name}” and removed {scheduled.Count} itinerary item(s)."
            : $"Deleted place “{place.Name}”.";

        activityLog.Add(
            tripId,
            actingMemberId,
            ActivityAction.PlaceDeleted,
            nameof(Place),
            place.Id,
            summary);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<Place>.Ok(place);
    }

    private async Task InvalidateTravelTimeCacheAsync(Guid placeId, CancellationToken cancellationToken)
    {
        var rows = await db.TravelTimeCaches
            .Where(c => c.FromPlaceId == placeId || c.ToPlaceId == placeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count > 0)
        {
            db.TravelTimeCaches.RemoveRange(rows);
        }
    }
}
