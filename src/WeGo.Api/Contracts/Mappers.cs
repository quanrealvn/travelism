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

    public static ExpenseResponse ToResponse(this Expense expense) =>
        new(
            expense.Id,
            expense.TripId,
            expense.Title,
            expense.Amount,
            expense.Currency,
            expense.PaidByMemberId,
            expense.Date,
            expense.Category.ToString(),
            expense.SplitType.ToString(),
            expense.Shares
                .OrderBy(s => s.MemberId)
                .Select(s => new ExpenseShareResponse(s.MemberId, s.ShareAmount))
                .ToList(),
            expense.CreatedAt,
            expense.UpdatedAt,
            expense.UpdatedByMemberId);

    public static BalanceResponse ToResponse(this WeGo.Api.Services.BalanceView view) =>
        new(
            view.Balances
                .Select(b => new MemberBalanceResponse(b.MemberId, b.Paid, b.Owed, b.Net))
                .ToList(),
            view.Transfers
                .Select(t => new TransferResponse(t.FromMemberId, t.ToMemberId, t.Amount))
                .ToList(),
            view.TotalSpent,
            view.Currency,
            view.CurrencyExponent);

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
