using WeGo.Domain.Entities;
using WeGo.Domain.Itinerary;
using WeGo.Domain.Money;
using WeGo.Domain.Places;

namespace WeGo.Api.Contracts;

/// <summary>Entity → DTO projection. Hand-written on purpose (spec §8: no AutoMapper).</summary>
public static class Mappers
{
    public static MemberResponse ToResponse(this Member member) =>
        new(member.Id, member.DisplayName, member.Role.ToString(), member.CreatedAt);

    public static TripResponse ToResponse(this Trip trip, IEnumerable<Member> members) =>
        new(
            trip.Id,
            trip.Name,
            trip.Destination,
            trip.StartDate,
            trip.EndDate,
            trip.TimeZoneId,
            trip.Currency,
            CurrencyInfo.GetExponent(trip.Currency),
            trip.BudgetAmount,
            trip.Status.ToString(),
            trip.InviteCode,
            members.OrderBy(m => m.CreatedAt).Select(ToResponse).ToList(),
            trip.CreatedAt,
            trip.UpdatedAt,
            trip.UpdatedByMemberId);

    public static ItineraryItemResponse ToResponse(this ItineraryItem item) =>
        new(
            item.Id,
            item.TripId,
            item.PlaceId,
            item.Place?.Name ?? string.Empty,
            item.Place?.Category.ToString() ?? string.Empty,
            item.Place?.EstimatedDurationMinutes ?? 0,
            item.Place?.Lat ?? 0,
            item.Place?.Lng ?? 0,
            item.Date,
            item.StartTime,
            item.Note,
            item.ActualCost,
            item.Place?.EstimatedCost,
            item.CreatedAt,
            item.UpdatedAt,
            item.UpdatedByMemberId);

    public static SuggestionGroupResponse ToResponse(this SuggestionGroup group) =>
        new(
            group.Slot.ToString(),
            group.Places
                .Select(p => new SuggestionResponse(
                    p.PlaceId, p.Name, p.Category.ToString(), p.EstimatedCost))
                .ToList());

    public static PlaceResponse ToResponse(this Place place) =>
        new(
            place.Id,
            place.TripId,
            place.Name,
            place.Lat,
            place.Lng,
            place.Category.ToString(),
            TimeSlotSet.ToNames(place.TimeSlots),
            place.EstimatedDurationMinutes,
            place.EstimatedCost,
            place.OpenHoursText,
            place.Status.ToString(),
            place.SkipReason,
            place.IsDeleted,
            place.Likes.Select(l => l.MemberId).OrderBy(id => id).ToList(),
            place.CreatedAt,
            place.UpdatedAt,
            place.UpdatedByMemberId);
}
