using WeGo.Domain.Places;

namespace WeGo.Domain.Itinerary;

public enum FeasibilityLevel
{
    Error = 0,
    Warning = 1,
    Info = 2,
}

/// <summary>Stable codes for feasibility findings (spec §5.2 and §7.3).</summary>
public static class FeasibilityCodes
{
    public const string Overlap = "OVERLAP";
    public const string InsufficientTravelTime = "INSUFFICIENT_TRAVEL_TIME";
    public const string IdleGap = "IDLE_GAP";
    public const string TimeSlotMismatch = "TIMESLOT_MISMATCH";
    public const string UnscheduledTime = "UNSCHEDULED_TIME";
    public const string CrossesMidnight = "CROSSES_MIDNIGHT";
}

/// <summary>One scheduled item, reduced to what the analysis needs.</summary>
public sealed record FeasibilityItem(
    Guid ItemId,
    Guid PlaceId,
    TimeOnly? StartTime,
    int DurationMinutes,
    TimeSlots PlaceTimeSlots,
    DateTimeOffset CreatedAt);

/// <summary>How long it takes to get from one place to another, and how we know.</summary>
public sealed record TravelLeg(int Minutes, TravelTimeSource Source);

public sealed record FeasibilityFinding(
    Guid ItineraryItemId,
    FeasibilityLevel Level,
    string Code,
    IReadOnlyDictionary<string, object?> Data);

/// <summary>
/// Spec §5.2, as a pure function. Given a day's items and the travel times
/// between them, says what will not work.
/// <para>
/// It never blocks a write — a plan is allowed to be wrong while you are still
/// making it. The point is to say so, not to prevent it.
/// </para>
/// </summary>
public static class Feasibility
{
    /// <summary>A gap longer than the travel plus this is dead time worth mentioning.</summary>
    public const int IdleGapThresholdMinutes = 90;

    /// <summary>
    /// Spec §7.3: an item running past midnight has its end clamped here for
    /// gap arithmetic in v1, rather than spilling into the next day.
    /// </summary>
    public static readonly TimeOnly LatestEndOfDay = new(23, 59);

    /// <param name="travelLookup">
    /// Travel time between two places, or null when unknown. Taking this as a
    /// function keeps the analysis pure: the caller does the cache reads and the
    /// routing calls, which must never happen inside a transaction (spec §5.4).
    /// </param>
    public static IReadOnlyList<FeasibilityFinding> Analyze(
        IReadOnlyList<FeasibilityItem> items,
        Func<Guid, Guid, TravelLeg?> travelLookup)
    {
        var findings = new List<FeasibilityFinding>();

        // Spec §7.2: CreatedAt breaks ties, so two items sharing a start time
        // always pair in the same order and the result is reproducible.
        var timed = items
            .Where(i => i.StartTime is not null)
            .OrderBy(i => i.StartTime!.Value)
            .ThenBy(i => i.CreatedAt)
            .ThenBy(i => i.ItemId)
            .ToList();

        foreach (var item in items.Where(i => i.StartTime is null).OrderBy(i => i.CreatedAt))
        {
            findings.Add(new FeasibilityFinding(
                item.ItemId,
                FeasibilityLevel.Info,
                FeasibilityCodes.UnscheduledTime,
                new Dictionary<string, object?>()));
        }

        foreach (var item in timed)
        {
            AddTimeSlotFinding(findings, item);
            AddMidnightFinding(findings, item);
        }

        // Spec §7.1: zero or one timed item means no pairs, and no crash.
        for (var i = 0; i + 1 < timed.Count; i++)
        {
            AddPairFindings(findings, timed[i], timed[i + 1], travelLookup);
        }

        return findings;
    }

    private static void AddTimeSlotFinding(List<FeasibilityFinding> findings, FeasibilityItem item)
    {
        var startTime = item.StartTime!.Value;
        if (TimeSlotSet.Matches(item.PlaceTimeSlots, startTime))
        {
            return;
        }

        findings.Add(new FeasibilityFinding(
            item.ItemId,
            FeasibilityLevel.Warning,
            FeasibilityCodes.TimeSlotMismatch,
            new Dictionary<string, object?>
            {
                ["startTime"] = startTime.ToString("HH:mm"),
                ["actualSlot"] = TimeSlotSet.ForTime(startTime).ToString(),
                ["placeSlots"] = TimeSlotSet.ToNames(item.PlaceTimeSlots),
            }));
    }

    private static void AddMidnightFinding(List<FeasibilityFinding> findings, FeasibilityItem item)
    {
        if (!CrossesMidnight(item))
        {
            return;
        }

        findings.Add(new FeasibilityFinding(
            item.ItemId,
            FeasibilityLevel.Info,
            FeasibilityCodes.CrossesMidnight,
            new Dictionary<string, object?>
            {
                ["startTime"] = item.StartTime!.Value.ToString("HH:mm"),
                ["durationMinutes"] = item.DurationMinutes,
                ["clampedEnd"] = LatestEndOfDay.ToString("HH:mm"),
            }));
    }

    private static void AddPairFindings(
        List<FeasibilityFinding> findings,
        FeasibilityItem first,
        FeasibilityItem second,
        Func<Guid, Guid, TravelLeg?> travelLookup)
    {
        var endOfFirst = EndOf(first);
        var startOfSecond = second.StartTime!.Value;

        // Subtracting two TimeOnly values is NOT signed: the operator wraps
        // around the clock, so 10:00 - 11:00 yields 23 hours rather than -1.
        // Every overlap would read as a comfortable gap. Going through
        // ToTimeSpan keeps the difference signed, which is what "gap < 0" needs.
        var gap = (int)(startOfSecond.ToTimeSpan() - endOfFirst.ToTimeSpan()).TotalMinutes;

        // Attached to the later item: it is the one that cannot start when it
        // says it will, and the one a traveller would move.
        if (gap < 0)
        {
            findings.Add(new FeasibilityFinding(
                second.ItemId,
                FeasibilityLevel.Error,
                FeasibilityCodes.Overlap,
                new Dictionary<string, object?>
                {
                    ["previousItemId"] = first.ItemId,
                    ["overlapMinutes"] = -gap,
                    ["previousEnds"] = endOfFirst.ToString("HH:mm"),
                    ["startsAt"] = startOfSecond.ToString("HH:mm"),
                }));
            return;
        }

        var leg = travelLookup(first.PlaceId, second.PlaceId);
        if (leg is null)
        {
            return;
        }

        if (gap < leg.Minutes)
        {
            findings.Add(new FeasibilityFinding(
                second.ItemId,
                FeasibilityLevel.Error,
                FeasibilityCodes.InsufficientTravelTime,
                new Dictionary<string, object?>
                {
                    ["previousItemId"] = first.ItemId,
                    ["gapMinutes"] = gap,
                    ["travelMinutes"] = leg.Minutes,
                    // Surfaced so the UI can mark an estimate as such (spec §5.4).
                    ["source"] = leg.Source.ToString().ToLowerInvariant(),
                }));
            return;
        }

        if (gap > leg.Minutes + IdleGapThresholdMinutes)
        {
            findings.Add(new FeasibilityFinding(
                second.ItemId,
                FeasibilityLevel.Info,
                FeasibilityCodes.IdleGap,
                new Dictionary<string, object?>
                {
                    ["previousItemId"] = first.ItemId,
                    ["gapMinutes"] = gap,
                    ["travelMinutes"] = leg.Minutes,
                    ["idleMinutes"] = gap - leg.Minutes,
                    ["source"] = leg.Source.ToString().ToLowerInvariant(),
                }));
        }
    }

    private static bool CrossesMidnight(FeasibilityItem item) =>
        item.StartTime!.Value.ToTimeSpan() + TimeSpan.FromMinutes(item.DurationMinutes)
        > TimeSpan.FromDays(1);

    /// <summary>
    /// When an item runs past midnight its end is clamped to 23:59 (spec §7.3),
    /// so the gap to the next item stays a same-day subtraction and cannot come
    /// out as a large positive number by wrapping around the clock.
    /// </summary>
    private static TimeOnly EndOf(FeasibilityItem item) =>
        CrossesMidnight(item)
            ? LatestEndOfDay
            : item.StartTime!.Value.AddMinutes(item.DurationMinutes);
}
