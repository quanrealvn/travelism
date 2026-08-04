namespace WeGo.Domain;

public enum TripStatus
{
    Planning = 0,
    Ongoing = 1,
    Completed = 2,
}

public enum MemberRole
{
    Owner = 0,
    Editor = 1,
}

public enum PlaceCategory
{
    Food = 0,
    Sight = 1,
    Photo = 2,
    Rest = 3,
    Other = 4,
}

/// <summary>
/// Time-of-day buckets a place is suitable for. Flags: a place may fit several.
/// Boundaries (spec 5.2): Morning 05:00-10:59, Noon 11:00-13:59,
/// Afternoon 14:00-17:59, Evening 18:00-23:59 plus 00:00-04:59.
/// </summary>
[Flags]
public enum TimeSlots
{
    None = 0,
    Morning = 1,
    Noon = 2,
    Afternoon = 4,
    Evening = 8,
}

public enum PlaceStatus
{
    Idea = 0,
    Shortlist = 1,
    Confirmed = 2,
    Visited = 3,
    Skipped = 4,
}

public enum TravelTimeMode
{
    Driving = 0,
}

public enum TravelTimeSource
{
    Osrm = 0,
    Haversine = 1,
}

public enum ActivityAction
{
    TripCreated = 0,
    TripUpdated = 1,
    MemberJoined = 2,
    PlaceCreated = 3,
    PlaceUpdated = 4,
    PlaceDeleted = 5,
    PlaceLiked = 6,
    PlaceUnliked = 7,
    PlaceStatusChanged = 8,
    ForceConfirmed = 9,
    ItineraryItemCreated = 10,
    ItineraryItemUpdated = 11,
    ItineraryItemDeleted = 12,
    ExpenseCreated = 13,
    ExpenseUpdated = 14,
    ExpenseDeleted = 15,
}
