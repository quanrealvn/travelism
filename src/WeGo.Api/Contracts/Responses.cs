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

/// <summary>
/// One row in the trip switcher. Deliberately smaller than <see cref="TripResponse"/>:
/// a browser may hold twenty of these, and the invite code in particular has no
/// business being sent for a trip nobody has opened.
/// </summary>
/// <param name="PlaceCount">Places on the wishlist, so a trip reads as started or empty.</param>
public sealed record TripSummaryResponse(
    Guid Id,
    string Name,
    string Destination,
    DateOnly StartDate,
    DateOnly EndDate,
    string Currency,
    int CurrencyExponent,
    long? BudgetAmount,
    string Status,
    int MemberCount,
    int PlaceCount,
    DateTimeOffset UpdatedAt);

/// <param name="DisplayName">
/// The label if there is one, otherwise the host — so a list of links never
/// shows a row of 200-character URLs.
/// </param>
public sealed record PlaceReferenceResponse(Guid Id, string Url, string? Label, string DisplayName);

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
    string? Description,
    IReadOnlyList<PlaceReferenceResponse> References,
    string Status,
    string? SkipReason,
    bool IsDeleted,
    IReadOnlyList<Guid> LikedByMemberIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid UpdatedByMemberId);

/// <summary>A candidate location from the place-name search.</summary>
/// <param name="Name">Short label, prefilled into the place name field.</param>
/// <param name="DisplayName">Full address, so near-identical names are distinguishable.</param>
/// <param name="Kind">Upstream classification such as "restaurant"; may be null.</param>
/// <param name="DistanceKm">
/// Straight-line distance from the trip's existing places, or null when the trip
/// has none to measure from. Surfaced because a free-text search for a
/// Vietnamese name can return a confident match on another continent — showing
/// the distance is what makes that visibly wrong instead of plausible.
/// </param>
public sealed record GeocodeResultResponse(
    string Name,
    string DisplayName,
    double Lat,
    double Lng,
    string? Kind,
    double? DistanceKm);

/// <summary>A place scheduled on a particular day of the trip.</summary>
public sealed record ItineraryItemResponse(
    Guid Id,
    Guid TripId,
    Guid PlaceId,
    string PlaceName,
    string PlaceCategory,
    int EstimatedDurationMinutes,
    double Lat,
    double Lng,
    DateOnly Date,
    TimeOnly? StartTime,
    string? Note,
    long? ActualCost,
    long? EstimatedCost,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid UpdatedByMemberId);

/// <summary>One thing wrong (or worth knowing) about a day's plan (spec §5.2).</summary>
public sealed record FeasibilityFindingResponse(
    Guid ItineraryItemId,
    string Level,
    string Code,
    IReadOnlyDictionary<string, object?> Data);

public sealed record FeasibilityResponse(IReadOnlyList<FeasibilityFindingResponse> Items);

/// <summary>Suggestions for one time of day (spec §5.1).</summary>
public sealed record SuggestionGroupResponse(string Slot, IReadOnlyList<SuggestionResponse> Places);

public sealed record SuggestionResponse(
    Guid PlaceId,
    string Name,
    string Category,
    long? EstimatedCost);

public sealed record ExpenseShareResponse(Guid MemberId, long ShareAmount);

public sealed record ExpenseResponse(
    Guid Id,
    Guid TripId,
    string Title,
    long Amount,
    string Currency,
    Guid PaidByMemberId,
    DateOnly Date,
    string Category,
    string SplitType,
    IReadOnlyList<ExpenseShareResponse> Shares,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid UpdatedByMemberId);

/// <summary>Per-member position. Net is positive when the trip owes them.</summary>
public sealed record MemberBalanceResponse(Guid MemberId, long Paid, long Owed, long Net);

public sealed record TransferResponse(Guid FromMemberId, Guid ToMemberId, long Amount);

public sealed record BalanceResponse(
    IReadOnlyList<MemberBalanceResponse> Balances,
    IReadOnlyList<TransferResponse> Transfers,
    long TotalSpent,
    string Currency,
    int CurrencyExponent);

/// <summary>
/// The whole trip in one response (spec §5.8). A reconnecting client replaces
/// its state with this rather than replaying events it may have missed.
/// </summary>
public sealed record SnapshotResponse(
    TripResponse Trip,
    IReadOnlyList<PlaceResponse> Places,
    IReadOnlyList<ItineraryItemResponse> Itinerary,
    IReadOnlyList<ExpenseResponse> Expenses,
    BalanceResponse Balance);

public sealed record DailyWeatherResponse(
    DateOnly Date,
    double? MaxTempC,
    double? MinTempC,
    double? PrecipitationMm,
    int? WeatherCode);

/// <param name="Stale">
/// True when the upstream service was unreachable and this came from cache
/// (spec §5.5). The client must say so rather than present it as current.
/// </param>
public sealed record WeatherResponse(
    double Lat,
    double Lng,
    string TimeZoneId,
    bool Stale,
    IReadOnlyList<DailyWeatherResponse> Days);

/// <summary>An entry in the trip's audit trail.</summary>
public sealed record ActivityResponse(
    Guid Id,
    Guid MemberId,
    string Action,
    string EntityType,
    Guid EntityId,
    string SummaryText,
    DateTimeOffset At);

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
