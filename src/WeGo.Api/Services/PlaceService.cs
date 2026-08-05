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
            .Include(p => p.References)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<Place>> GetAsync(Guid tripId, Guid placeId, CancellationToken cancellationToken)
    {
        var place = await db.Places
            .AsNoTracking()
            .Include(p => p.Likes)
            .Include(p => p.References)
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
            request.OpenHoursText,
            request.Description,
            ToReferenceInputs(request.References));

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
            Description = draft.Description,
            // A new place starts as an unvetted Idea; only likes promote it
            // (spec §4). Recorded in DECISIONS.md — the spec does not name a
            // default explicitly.
            Status = PlaceStatus.Idea,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByMemberId = actingMemberId,
        };

        ApplyReferences(place, draft.References);

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
            .Include(p => p.References)
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
            request.OpenHoursText.IsSet ? request.OpenHoursText.Value : place.OpenHoursText,
            request.Description.IsSet ? request.Description.Value : place.Description,
            request.References.IsSet
                ? ToReferenceInputs(request.References.Value)
                : place.References
                    .OrderBy(r => r.SortOrder)
                    .Select(r => new ReferenceInput(r.Url, r.Label))
                    .ToList());

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
        place.Description = draft.Description;
        place.UpdatedAt = clock.UtcNow;
        place.UpdatedByMemberId = actingMemberId;

        if (request.References.IsSet)
        {
            // Replaced wholesale: the editor works on the list as a whole, and
            // per-link patching would need stable ids on the client for no gain.
            //
            // Done through the DbSet rather than the navigation collection.
            // Mutating a tracked collection makes EF's fixup both sever the
            // relationship and cascade-delete the orphan, which issues two
            // DELETEs for one row — the second affects nothing and throws
            // DbUpdateConcurrencyException.
            db.PlaceReferences.RemoveRange(place.References.ToList());

            for (var i = 0; i < draft.References.Count; i++)
            {
                db.PlaceReferences.Add(new PlaceReference
                {
                    Id = Guid.NewGuid(),
                    PlaceId = place.Id,
                    Url = draft.References[i].Url,
                    Label = draft.References[i].Label,
                    SortOrder = i,
                });
            }
        }

        activityLog.Add(
            tripId,
            actingMemberId,
            ActivityAction.PlaceUpdated,
            nameof(Place),
            place.Id,
            $"Updated place “{place.Name}”.");

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!request.References.IsSet)
        {
            return Result<Place>.Ok(place);
        }

        // The tracked navigation still holds the links that were just deleted,
        // so the response is read fresh rather than reported from memory.
        db.ChangeTracker.Clear();
        return await GetAsync(tripId, placeId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Spec §4: liking is idempotent, and a like may promote the place —
    /// to Shortlist on the first, to Confirmed once everyone has liked it.
    /// </summary>
    public async Task<Result<Place>> LikeAsync(
        Guid tripId,
        Guid placeId,
        Guid actingMemberId,
        CancellationToken cancellationToken) =>
        await SetLikeAsync(tripId, placeId, actingMemberId, liked: true, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Removing a like may return the place to Idea, but never demotes one that
    /// is already Confirmed — see <see cref="PlaceStateMachine.StatusForLikes"/>.
    /// </summary>
    public async Task<Result<Place>> UnlikeAsync(
        Guid tripId,
        Guid placeId,
        Guid actingMemberId,
        CancellationToken cancellationToken) =>
        await SetLikeAsync(tripId, placeId, actingMemberId, liked: false, cancellationToken)
            .ConfigureAwait(false);

    private async Task<Result<Place>> SetLikeAsync(
        Guid tripId,
        Guid placeId,
        Guid actingMemberId,
        bool liked,
        CancellationToken cancellationToken)
    {
        var place = await db.Places
            .Include(p => p.Likes)
            .Include(p => p.References)
            .FirstOrDefaultAsync(p => p.Id == placeId && p.TripId == tripId, cancellationToken)
            .ConfigureAwait(false);

        if (place is null)
        {
            return Failure.NotFound("Place not found.");
        }

        var existing = place.Likes.FirstOrDefault(l => l.MemberId == actingMemberId);
        var now = clock.UtcNow;

        // Spec §4: liking twice is a no-op, and so is unliking what you never
        // liked. Both return the current state rather than an error.
        if (liked && existing is null)
        {
            place.Likes.Add(new PlaceLike
            {
                PlaceId = place.Id,
                MemberId = actingMemberId,
                CreatedAt = now,
            });
        }
        else if (!liked && existing is not null)
        {
            place.Likes.Remove(existing);
            db.PlaceLikes.Remove(existing);
        }
        else
        {
            return Result<Place>.Ok(place);
        }

        var memberCount = await db.Members
            .CountAsync(m => m.TripId == tripId, cancellationToken)
            .ConfigureAwait(false);

        var previousStatus = place.Status;
        place.Status = PlaceStateMachine.StatusForLikes(place.Status, place.Likes.Count, memberCount);
        place.UpdatedAt = now;
        place.UpdatedByMemberId = actingMemberId;

        activityLog.Add(
            tripId,
            actingMemberId,
            liked ? ActivityAction.PlaceLiked : ActivityAction.PlaceUnliked,
            nameof(Place),
            place.Id,
            liked ? $"Liked “{place.Name}”." : $"Removed like from “{place.Name}”.");

        if (place.Status != previousStatus)
        {
            activityLog.Add(
                tripId,
                actingMemberId,
                ActivityAction.PlaceStatusChanged,
                nameof(Place),
                place.Id,
                $"“{place.Name}” moved from {previousStatus} to {place.Status}.");
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (SqliteErrorDetection.IsUniqueConstraintViolation(ex))
        {
            // Two clients liked at the same moment. The composite key made one
            // of them redundant, which is exactly the idempotent outcome.
            db.ChangeTracker.Clear();
            return await GetAsync(tripId, placeId, cancellationToken).ConfigureAwait(false);
        }

        return Result<Place>.Ok(place);
    }

    /// <summary>
    /// An explicit status change: force-confirming, un-confirming, or recording
    /// that a place was visited or skipped. Every edge is checked against
    /// spec §4 before anything is written.
    /// </summary>
    public async Task<Result<Place>> ChangeStatusAsync(
        Guid tripId,
        Guid placeId,
        Guid actingMemberId,
        string? requestedStatus,
        string? skipReason,
        CancellationToken cancellationToken)
    {
        var validation = new ValidationResult();
        var target = EnumInput.Required<PlaceStatus>(validation, "status", requestedStatus);
        var validSkipReason = StringInput.Optional(
            validation, "skipReason", skipReason, PlaceDefaults.SkipReasonMaxLength);

        if (!validation.IsValid || target is null)
        {
            return Failure.Validation(validation);
        }

        var place = await db.Places
            .Include(p => p.Likes)
            .Include(p => p.References)
            .FirstOrDefaultAsync(p => p.Id == placeId && p.TripId == tripId, cancellationToken)
            .ConfigureAwait(false);

        if (place is null)
        {
            return Failure.NotFound("Place not found.");
        }

        var tripStatus = await db.Trips
            .Where(t => t.Id == tripId)
            .Select(t => t.Status)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        var refusal = PlaceStateMachine.CheckManual(
            place.Status, target.Value, new PlaceTransitionContext(tripStatus));

        if (refusal != TransitionRefusal.None)
        {
            return Failure.Conflict(
                ErrorCodes.InvalidStatusTransition,
                refusal switch
                {
                    TransitionRefusal.TripNotStarted =>
                        $"“{place.Name}” cannot be marked {target.Value} until the trip is Ongoing.",
                    TransitionRefusal.LikeDriven =>
                        $"“{place.Name}” moves between Idea and Shortlist by liking, not directly.",
                    _ => $"“{place.Name}” cannot go from {place.Status} to {target.Value}.",
                },
                new Dictionary<string, object?>
                {
                    ["from"] = place.Status.ToString(),
                    ["to"] = target.Value.ToString(),
                    ["reason"] = refusal.ToString(),
                });
        }

        var previousStatus = place.Status;

        // Spec §4 asks for a force-confirm to be logged as such: it records that
        // a person decided, rather than that everyone agreed.
        var forced = previousStatus == PlaceStatus.Shortlist && target.Value == PlaceStatus.Confirmed;

        place.Status = target.Value;
        // A reason only belongs to a skip; moving away from Skipped clears it.
        place.SkipReason = target.Value == PlaceStatus.Skipped ? validSkipReason : null;
        place.UpdatedAt = clock.UtcNow;
        place.UpdatedByMemberId = actingMemberId;

        activityLog.Add(
            tripId,
            actingMemberId,
            forced ? ActivityAction.ForceConfirmed : ActivityAction.PlaceStatusChanged,
            nameof(Place),
            place.Id,
            forced
                ? $"Force-confirmed “{place.Name}” without waiting for everyone."
                : $"“{place.Name}” moved from {previousStatus} to {place.Status}.");

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
            .Include(p => p.References)
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

    private static IReadOnlyList<ReferenceInput> ToReferenceInputs(
        IReadOnlyList<PlaceReferenceRequest>? references) =>
        references?.Select(r => new ReferenceInput(r.Url, r.Label)).ToList() ?? [];

    /// <summary>Writes validated links onto the place, numbered in order.</summary>
    private static void ApplyReferences(Place place, IReadOnlyList<ReferenceDraft> references)
    {
        for (var i = 0; i < references.Count; i++)
        {
            place.References.Add(new PlaceReference
            {
                Id = Guid.NewGuid(),
                PlaceId = place.Id,
                Url = references[i].Url,
                Label = references[i].Label,
                SortOrder = i,
            });
        }
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
