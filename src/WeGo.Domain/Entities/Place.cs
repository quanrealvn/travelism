using WeGo.Domain.Common;

namespace WeGo.Domain.Entities;

public sealed class Place : Entity
{
    public Guid TripId { get; set; }

    public required string Name { get; set; }

    public double Lat { get; set; }

    public double Lng { get; set; }

    public PlaceCategory Category { get; set; }

    /// <summary>At least one slot is always set (enforced by <c>PlaceRules</c>).</summary>
    public TimeSlots TimeSlots { get; set; }

    public int EstimatedDurationMinutes { get; set; }

    /// <summary>Minor units of the trip currency, or null when unknown.</summary>
    public long? EstimatedCost { get; set; }

    public string? OpenHoursText { get; set; }

    /// <summary>Free text: why this place is worth going to.</summary>
    public string? Description { get; set; }

    /// <summary>Links backing up the description, in the order they were added.</summary>
    public List<PlaceReference> References { get; } = [];

    public PlaceStatus Status { get; set; } = PlaceStatus.Idea;

    public string? SkipReason { get; set; }

    /// <summary>Soft delete (spec §5.6: places are never hard-deleted).</summary>
    public bool IsDeleted { get; set; }

    public List<PlaceLike> Likes { get; } = [];
}

public static class PlaceDefaults
{
    public const int NameMaxLength = 120;
    public const int OpenHoursTextMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int SkipReasonMaxLength = 300;
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 1440;
}
