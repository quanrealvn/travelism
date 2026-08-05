using Microsoft.EntityFrameworkCore;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Errors;
using WeGo.Domain.Abstractions;
using WeGo.Domain.Common;
using WeGo.Domain.Entities;
using WeGo.Domain.Itinerary;
using WeGo.Domain.Places;
using WeGo.Infrastructure.Persistence;

namespace WeGo.Api.Services;

public sealed class ItineraryService(
    WeGoDbContext db,
    IClock clock,
    ActivityLogWriter activityLog,
    TravelTimeService travelTimes)
{
    /// <summary>
    /// Spec §5.2. A pure read: it never blocks a write, because a plan is
    /// allowed to be wrong while you are still making it.
    /// </summary>
    public async Task<Result<IReadOnlyList<FeasibilityFinding>>> FeasibilityAsync(
        Guid tripId,
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        if (date is null)
        {
            var missing = new ValidationResult();
            missing.Add("date", FieldErrorCodes.Required, "'date' is required (format YYYY-MM-DD).");
            return Failure.Validation(missing);
        }

        var trip = await db.Trips
            .AsNoTracking()
            .FirstAsync(t => t.Id == tripId, cancellationToken)
            .ConfigureAwait(false);

        if (!trip.ContainsDate(date.Value))
        {
            return DateOutOfRange(trip, date.Value);
        }

        var items = await db.ItineraryItems
            .AsNoTracking()
            .Where(i => i.TripId == tripId && i.Date == date.Value)
            .Select(i => new FeasibilityItem(
                i.Id,
                i.PlaceId,
                i.StartTime,
                i.Place!.EstimatedDurationMinutes,
                i.Place.TimeSlots,
                i.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Spec §5.4: at most (n-1) lookups for a day, cache-first. The pairs are
        // computed the same way the analyzer walks them.
        var ordered = items
            .Where(i => i.StartTime is not null)
            .OrderBy(i => i.StartTime!.Value)
            .ThenBy(i => i.CreatedAt)
            .ThenBy(i => i.ItemId)
            .ToList();

        var pairs = new List<(Guid From, Guid To)>();
        for (var i = 0; i + 1 < ordered.Count; i++)
        {
            pairs.Add((ordered[i].PlaceId, ordered[i + 1].PlaceId));
        }

        var legs = await travelTimes.GetLegsAsync(tripId, pairs, cancellationToken).ConfigureAwait(false);

        var findings = Feasibility.Analyze(
            items,
            (from, to) => legs.TryGetValue((from, to), out var leg) ? leg : null);

        return Result<IReadOnlyList<FeasibilityFinding>>.Ok(findings);
    }

    public async Task<IReadOnlyList<ItineraryItem>> ListAsync(
        Guid tripId,
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        var query = db.ItineraryItems
            .AsNoTracking()
            .Include(i => i.Place)
            .Where(i => i.TripId == tripId);

        if (date is { } onDate)
        {
            query = query.Where(i => i.Date == onDate);
        }

        // Spec §7.2: CreatedAt breaks ties so two items at the same time always
        // pair in the same order.
        return await query
            .OrderBy(i => i.Date)
            .ThenBy(i => i.StartTime == null)
            .ThenBy(i => i.StartTime)
            .ThenBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<ItineraryItem>> CreateAsync(
        Guid tripId,
        Guid actingMemberId,
        CreateItineraryItemRequest request,
        CancellationToken cancellationToken)
    {
        var trip = await db.Trips
            .AsNoTracking()
            .FirstAsync(t => t.Id == tripId, cancellationToken)
            .ConfigureAwait(false);

        var validation = new ValidationResult();

        if (request.PlaceId is null)
        {
            validation.Add("placeId", FieldErrorCodes.Required, "'placeId' is required.");
        }

        if (request.Date is null)
        {
            validation.Add("date", FieldErrorCodes.Required, "'date' is required (format YYYY-MM-DD).");
        }

        var note = StringInput.Optional(
            validation, "note", request.Note, ItineraryItemDefaults.NoteMaxLength);

        if (request.ActualCost is < 0)
        {
            validation.Add("actualCost", FieldErrorCodes.OutOfRange, "'actualCost' cannot be negative.");
        }

        if (!validation.IsValid || request.PlaceId is null || request.Date is null)
        {
            return Failure.Validation(validation);
        }

        var date = request.Date.Value;
        if (!trip.ContainsDate(date))
        {
            return DateOutOfRange(trip, date);
        }

        // Scoped by trip id: a place from another trip must read as "not found"
        // rather than be schedulable here.
        var place = await db.Places
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PlaceId.Value && p.TripId == tripId, cancellationToken)
            .ConfigureAwait(false);

        if (place is null)
        {
            return Failure.NotFound("Place not found on this trip.");
        }

        var duplicate = await db.ItineraryItems
            .AnyAsync(
                i => i.TripId == tripId && i.PlaceId == place.Id && i.Date == date,
                cancellationToken)
            .ConfigureAwait(false);

        if (duplicate)
        {
            return DuplicateOnDate(place, date);
        }

        var now = clock.UtcNow;
        var item = new ItineraryItem
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            PlaceId = place.Id,
            Date = date,
            StartTime = request.StartTime,
            Note = note,
            ActualCost = request.ActualCost,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByMemberId = actingMemberId,
        };

        db.ItineraryItems.Add(item);
        activityLog.Add(
            tripId,
            actingMemberId,
            ActivityAction.ItineraryItemCreated,
            nameof(ItineraryItem),
            item.Id,
            $"Scheduled “{place.Name}” on {date:yyyy-MM-dd}.");

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (SqliteErrorDetection.IsUniqueConstraintViolation(ex))
        {
            // Lost a race with a concurrent add of the same place on the same
            // day; the unique index is what actually enforces §6.
            db.ChangeTracker.Clear();
            return DuplicateOnDate(place, date);
        }

        item.Place = place;
        return Result<ItineraryItem>.Ok(item);
    }

    /// <summary>
    /// Moving an item to another day, giving it a time, or editing its note.
    /// This is what a drag-and-drop drop lands on.
    /// </summary>
    public async Task<Result<ItineraryItem>> UpdateAsync(
        Guid tripId,
        Guid itemId,
        Guid actingMemberId,
        UpdateItineraryItemRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.ItineraryItems
            .Include(i => i.Place)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.TripId == tripId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Failure.NotFound("Itinerary item not found.");
        }

        var trip = await db.Trips
            .AsNoTracking()
            .FirstAsync(t => t.Id == tripId, cancellationToken)
            .ConfigureAwait(false);

        var validation = new ValidationResult();
        var note = request.Note.IsSet
            ? StringInput.Optional(validation, "note", request.Note.Value, ItineraryItemDefaults.NoteMaxLength)
            : item.Note;

        var actualCost = request.ActualCost.IsSet ? request.ActualCost.Value : item.ActualCost;
        if (actualCost is < 0)
        {
            validation.Add("actualCost", FieldErrorCodes.OutOfRange, "'actualCost' cannot be negative.");
        }

        var date = request.Date.IsSet && request.Date.Value is { } newDate ? newDate : item.Date;

        if (request.Date.IsSet && request.Date.Value is null)
        {
            validation.Add("date", FieldErrorCodes.Required, "'date' cannot be cleared.");
        }

        if (!validation.IsValid)
        {
            return Failure.Validation(validation);
        }

        if (!trip.ContainsDate(date))
        {
            return DateOutOfRange(trip, date);
        }

        if (date != item.Date)
        {
            var duplicate = await db.ItineraryItems
                .AnyAsync(
                    i => i.TripId == tripId && i.PlaceId == item.PlaceId && i.Date == date && i.Id != item.Id,
                    cancellationToken)
                .ConfigureAwait(false);

            if (duplicate)
            {
                return DuplicateOnDate(item.Place!, date);
            }
        }

        var movedFrom = item.Date;

        item.Date = date;
        item.StartTime = request.StartTime.IsSet ? request.StartTime.Value : item.StartTime;
        item.Note = note;
        item.ActualCost = actualCost;
        item.UpdatedAt = clock.UtcNow;
        item.UpdatedByMemberId = actingMemberId;

        activityLog.Add(
            tripId,
            actingMemberId,
            ActivityAction.ItineraryItemUpdated,
            nameof(ItineraryItem),
            item.Id,
            movedFrom == item.Date
                ? $"Updated “{item.Place?.Name}” on {item.Date:yyyy-MM-dd}."
                : $"Moved “{item.Place?.Name}” from {movedFrom:yyyy-MM-dd} to {item.Date:yyyy-MM-dd}.");

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (SqliteErrorDetection.IsUniqueConstraintViolation(ex))
        {
            db.ChangeTracker.Clear();
            return DuplicateOnDate(item.Place!, date);
        }

        return Result<ItineraryItem>.Ok(item);
    }

    /// <summary>Spec §5.6: itinerary items are hard-deleted, and logged.</summary>
    public async Task<Result<ItineraryItem>> DeleteAsync(
        Guid tripId,
        Guid itemId,
        Guid actingMemberId,
        CancellationToken cancellationToken)
    {
        var item = await db.ItineraryItems
            .Include(i => i.Place)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.TripId == tripId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Failure.NotFound("Itinerary item not found.");
        }

        db.ItineraryItems.Remove(item);
        activityLog.Add(
            tripId,
            actingMemberId,
            ActivityAction.ItineraryItemDeleted,
            nameof(ItineraryItem),
            item.Id,
            $"Removed “{item.Place?.Name}” from {item.Date:yyyy-MM-dd}.");

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<ItineraryItem>.Ok(item);
    }

    /// <summary>Spec §5.1.</summary>
    public async Task<Result<IReadOnlyList<SuggestionGroup>>> SuggestAsync(
        Guid tripId,
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        if (date is null)
        {
            var missing = new ValidationResult();
            missing.Add("date", FieldErrorCodes.Required, "'date' is required (format YYYY-MM-DD).");
            return Failure.Validation(missing);
        }

        var trip = await db.Trips
            .AsNoTracking()
            .FirstAsync(t => t.Id == tripId, cancellationToken)
            .ConfigureAwait(false);

        if (!trip.ContainsDate(date.Value))
        {
            return DateOutOfRange(trip, date.Value);
        }

        var scheduled = await db.ItineraryItems
            .AsNoTracking()
            .Where(i => i.TripId == tripId && i.Date == date.Value)
            .Select(i => new { i.PlaceId, i.StartTime, Category = i.Place!.Category })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var scheduledPlaceIds = scheduled.Select(s => s.PlaceId).ToHashSet();

        // The query filter already excludes soft-deleted places (spec §6).
        var candidates = await db.Places
            .AsNoTracking()
            .Where(p => p.TripId == tripId && p.Status == PlaceStatus.Confirmed)
            .Select(p => new { p.Id, p.Name, p.Category, p.TimeSlots, p.EstimatedCost })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var available = candidates
            .Where(p => !scheduledPlaceIds.Contains(p.Id))
            .Select(p => new SuggestionCandidate(p.Id, p.Name, p.Category, p.TimeSlots, p.EstimatedCost))
            .ToList();

        var alreadyScheduled = scheduled
            .Select(s => new ScheduledPlace(s.PlaceId, s.Category, s.StartTime))
            .ToList();

        return Result<IReadOnlyList<SuggestionGroup>>.Ok(Suggestions.Build(available, alreadyScheduled));
    }

    private static Failure DateOutOfRange(Trip trip, DateOnly date) =>
        Failure.Unprocessable(
            ErrorCodes.DateOutOfRange,
            $"{date:yyyy-MM-dd} is outside the trip, which runs "
                + $"{trip.StartDate:yyyy-MM-dd} to {trip.EndDate:yyyy-MM-dd}.",
            new Dictionary<string, object?>
            {
                ["startDate"] = trip.StartDate.ToString("yyyy-MM-dd"),
                ["endDate"] = trip.EndDate.ToString("yyyy-MM-dd"),
            });

    private static Failure DuplicateOnDate(Place place, DateOnly date) =>
        Failure.Conflict(
            ErrorCodes.DuplicatePlaceOnDate,
            $"“{place.Name}” is already scheduled on {date:yyyy-MM-dd}.",
            new Dictionary<string, object?> { ["date"] = date.ToString("yyyy-MM-dd") });
}
