using WeGo.Domain.Places;

namespace WeGo.Domain.Itinerary;

/// <summary>A confirmed place that could still be added to the day.</summary>
public sealed record SuggestionCandidate(
    Guid PlaceId,
    string Name,
    PlaceCategory Category,
    TimeSlots TimeSlots,
    long? EstimatedCost);

/// <summary>
/// Something already on the day's plan.
/// </summary>
/// <param name="StartTime">
/// Null means "sometime that day". Such an item is not counted against any
/// particular slot — it has not claimed a part of the day yet, so letting it
/// suppress same-category suggestions everywhere would hide good options.
/// </param>
public sealed record ScheduledPlace(Guid PlaceId, PlaceCategory Category, TimeOnly? StartTime);

/// <summary>One time-of-day bucket of suggestions.</summary>
public sealed record SuggestionGroup(TimeSlots Slot, IReadOnlyList<SuggestionCandidate> Places);

/// <summary>
/// Spec §5.1, as a pure function. What to suggest for a day, grouped by time of
/// day, with variety preferred over repetition.
/// </summary>
public static class Suggestions
{
    /// <summary>
    /// Builds the grouped suggestions for one date.
    /// </summary>
    /// <param name="candidates">
    /// Confirmed, not soft-deleted, and not already scheduled on this date —
    /// the caller filters, because that is a database question.
    /// </param>
    /// <param name="alreadyScheduled">Items already on the plan for this date.</param>
    public static IReadOnlyList<SuggestionGroup> Build(
        IReadOnlyList<SuggestionCandidate> candidates,
        IReadOnlyList<ScheduledPlace> alreadyScheduled)
    {
        var groups = new List<SuggestionGroup>(TimeSlotSet.All.Count);

        foreach (var slot in TimeSlotSet.All)
        {
            // A place offering several slots is a candidate in each of them
            // (spec §5.1), so this is a filter rather than a partition.
            var inSlot = candidates.Where(c => c.TimeSlots.HasFlag(slot)).ToList();
            if (inSlot.Count == 0)
            {
                groups.Add(new SuggestionGroup(slot, []));
                continue;
            }

            var categoriesTaken = CategoriesScheduledIn(slot, alreadyScheduled);

            var ordered = inSlot
                // (a) Variety first: something unlike what is already planned
                // for this part of the day beats a third café in a row.
                .OrderBy(c => categoriesTaken.Contains(c.Category) ? 1 : 0)
                // (b) Then cheapest first, with unknown costs last — an unpriced
                // place is not free, it is unknown, and sorting it first would
                // read as a recommendation.
                .ThenBy(c => c.EstimatedCost is null ? 1 : 0)
                .ThenBy(c => c.EstimatedCost ?? 0)
                // Total order, so the same inputs always produce the same list.
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ThenBy(c => c.PlaceId)
                .ToList();

            groups.Add(new SuggestionGroup(slot, ordered));
        }

        return groups;
    }

    /// <summary>
    /// Which categories already occupy a slot on this date. An item claims the
    /// slot its start time falls in; one with no time claims none.
    /// </summary>
    private static HashSet<PlaceCategory> CategoriesScheduledIn(
        TimeSlots slot,
        IReadOnlyList<ScheduledPlace> alreadyScheduled)
    {
        var categories = new HashSet<PlaceCategory>();

        foreach (var scheduled in alreadyScheduled)
        {
            if (scheduled.StartTime is { } startTime && TimeSlotSet.ForTime(startTime) == slot)
            {
                categories.Add(scheduled.Category);
            }
        }

        return categories;
    }
}
