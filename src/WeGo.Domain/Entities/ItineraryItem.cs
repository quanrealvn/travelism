using WeGo.Domain.Common;

namespace WeGo.Domain.Entities;

public sealed class ItineraryItem : Entity
{
    public Guid TripId { get; set; }

    public Guid PlaceId { get; set; }

    /// <summary>Calendar date in the trip timezone — never a DateTime (spec §7.10).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Wall-clock start in the trip timezone; null = "sometime that day".</summary>
    public TimeOnly? StartTime { get; set; }

    public string? Note { get; set; }

    /// <summary>Minor units of the trip currency.</summary>
    public long? ActualCost { get; set; }

    public Place? Place { get; set; }
}

public static class ItineraryItemDefaults
{
    public const int NoteMaxLength = 500;
}
