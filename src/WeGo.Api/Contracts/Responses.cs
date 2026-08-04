namespace WeGo.Api.Contracts;

/// <summary>
/// Response bodies. These are deliberately separate from the EF entities
/// (spec §8) — an entity is never serialized, so a new column cannot silently
/// become part of the public contract.
/// Enums are emitted as their names; money is emitted as integer minor units
/// with the currency exponent alongside, so the client formats without ever
/// doing float arithmetic on an amount.
/// </summary>
public sealed record MemberResponse(
    Guid Id,
    string DisplayName,
    string Role,
    DateTimeOffset CreatedAt);

public sealed record TripResponse(
    Guid Id,
    string Name,
    string Destination,
    DateOnly StartDate,
    DateOnly EndDate,
    string TimeZoneId,
    string Currency,
    int CurrencyExponent,
    long? BudgetAmount,
    string Status,
    string InviteCode,
    IReadOnlyList<MemberResponse> Members,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid UpdatedByMemberId);

public sealed record PlaceResponse(
    Guid Id,
    Guid TripId,
    string Name,
    double Lat,
    double Lng,
    string Category,
    IReadOnlyList<string> TimeSlots,
    int EstimatedDurationMinutes,
    long? EstimatedCost,
    string? OpenHoursText,
    string Status,
    string? SkipReason,
    bool IsDeleted,
    IReadOnlyList<Guid> LikedByMemberIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid UpdatedByMemberId);

/// <summary>Who the caller is, for a client that has a cookie but no state.</summary>
public sealed record SessionResponse(
    Guid TripId,
    Guid MemberId,
    string DisplayName,
    string Role);

/// <summary>Returned by trip creation and join: the session plus the trip it belongs to.</summary>
public sealed record TripSessionResponse(
    TripResponse Trip,
    SessionResponse Session);
