namespace WeGo.Api.Contracts;

/// <summary>
/// Request bodies. Every field is nullable so that "missing" is a value the
/// validator can see and report as a per-field 422 (spec §7.14) rather than
/// something the JSON layer rejects with its own non-conforming 400.
/// Enums arrive as strings and are parsed by the domain validators for the
/// same reason.
/// </summary>
public sealed record CreateTripRequest(
    string? Name,
    string? Destination,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? TimeZoneId,
    string? Currency,
    long? BudgetAmount,
    string? OwnerDisplayName);

public sealed record JoinTripRequest(
    string? InviteCode,
    string? DisplayName);

public sealed record UpdateTripRequest(
    Patch<string?> Name,
    Patch<string?> Destination,
    Patch<DateOnly?> StartDate,
    Patch<DateOnly?> EndDate,
    Patch<string?> TimeZoneId,
    Patch<long?> BudgetAmount,
    Patch<string?> Status);

/// <summary>A pasted map link or coordinate pair to turn into a location.</summary>
public sealed record ResolveLinkRequest(string? Url);

/// <summary>An explicit place status change (spec §4). SkipReason applies only to Skipped.</summary>
public sealed record ChangePlaceStatusRequest(string? Status, string? SkipReason);

public sealed record CreateItineraryItemRequest(
    Guid? PlaceId,
    DateOnly? Date,
    TimeOnly? StartTime,
    string? Note,
    long? ActualCost);

public sealed record ExpenseShareRequest(Guid? MemberId, long? ShareAmount);

public sealed record CreateExpenseRequest(
    string? Title,
    long? Amount,
    string? Currency,
    Guid? PaidByMemberId,
    DateOnly? Date,
    string? Category,
    string? SplitType,
    IReadOnlyList<ExpenseShareRequest>? Shares);

/// <summary>
/// Partial update of a scheduled item. <see cref="StartTime"/> uses
/// <see cref="Patch{T}"/> so an explicit null can clear the time — "sometime
/// that day" is a meaningful state, distinct from not mentioning the field.
/// </summary>
public sealed record UpdateItineraryItemRequest(
    Patch<DateOnly?> Date,
    Patch<TimeOnly?> StartTime,
    Patch<string?> Note,
    Patch<long?> ActualCost);

/// <summary>A reference link on a place. Label is optional; the host stands in.</summary>
public sealed record PlaceReferenceRequest(string? Url, string? Label);

public sealed record CreatePlaceRequest(
    string? Name,
    double? Lat,
    double? Lng,
    string? Category,
    string?[]? TimeSlots,
    int? EstimatedDurationMinutes,
    long? EstimatedCost,
    string? OpenHoursText,
    string? Description,
    IReadOnlyList<PlaceReferenceRequest>? References);

/// <summary>
/// Partial update. <see cref="References"/> replaces the whole list when sent —
/// per-link patching would need stable ids on the client for no real gain, and
/// the editor works on the list as a whole anyway.
/// </summary>
public sealed record UpdatePlaceRequest(
    Patch<string?> Name,
    Patch<double?> Lat,
    Patch<double?> Lng,
    Patch<string?> Category,
    Patch<string?[]?> TimeSlots,
    Patch<int?> EstimatedDurationMinutes,
    Patch<long?> EstimatedCost,
    Patch<string?> OpenHoursText,
    Patch<string?> Description,
    Patch<IReadOnlyList<PlaceReferenceRequest>?> References);
