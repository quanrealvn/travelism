namespace WeGo.Domain.Places;

/// <summary>
/// Helpers over the <see cref="TimeSlots"/> bitmask. Pure — the same rules back
/// the API payload today and the feasibility slot check in a later milestone.
/// </summary>
public static class TimeSlotSet
{
    /// <summary>Every selectable slot, in chronological order.</summary>
    public static readonly IReadOnlyList<TimeSlots> All =
    [
        TimeSlots.Morning,
        TimeSlots.Noon,
        TimeSlots.Afternoon,
        TimeSlots.Evening,
    ];

    /// <summary>Expands the bitmask into slot names, chronologically ordered.</summary>
    public static IReadOnlyList<string> ToNames(TimeSlots slots)
    {
        var names = new List<string>(All.Count);
        foreach (var slot in All)
        {
            if (slots.HasFlag(slot))
            {
                names.Add(slot.ToString());
            }
        }

        return names;
    }

    /// <summary>
    /// The slot a wall-clock time falls into (spec §5.2). The late-night hours
    /// 00:00–04:59 belong to Evening: they are the tail of the previous night
    /// out, not a fifth bucket.
    /// </summary>
    public static TimeSlots ForTime(TimeOnly time) => time.Hour switch
    {
        >= 5 and <= 10 => TimeSlots.Morning,
        >= 11 and <= 13 => TimeSlots.Noon,
        >= 14 and <= 17 => TimeSlots.Afternoon,
        _ => TimeSlots.Evening,
    };

    public static bool Matches(TimeSlots slots, TimeOnly time) => slots.HasFlag(ForTime(time));
}
