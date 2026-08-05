using WeGo.Api.Auth;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Services;

namespace WeGo.Api.Endpoints;

public static class ItineraryEndpoints
{
    public static void MapItineraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/trips/{tripId:guid}").AddEndpointFilter<TripMemberFilter>();

        group.MapGet("/itinerary", async (
            Guid tripId,
            DateOnly? date,
            ItineraryService itinerary,
            CancellationToken cancellationToken) =>
        {
            var items = await itinerary.ListAsync(tripId, date, cancellationToken);
            return Results.Ok(items.Select(i => i.ToResponse()).ToList());
        })
        .WithName("ListItinerary");

        group.MapPost("/itinerary", async (
            Guid tripId,
            CreateItineraryItemRequest request,
            ItineraryService itinerary,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await itinerary.CreateAsync(tripId, caller.MemberId, request, cancellationToken);
            return result.ToHttp(item =>
                Results.Created($"/trips/{tripId}/itinerary/{item.Id}", item.ToResponse()));
        })
        .WithName("CreateItineraryItem");

        group.MapPatch("/itinerary/{itemId:guid}", async (
            Guid tripId,
            Guid itemId,
            UpdateItineraryItemRequest request,
            ItineraryService itinerary,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await itinerary.UpdateAsync(
                tripId, itemId, caller.MemberId, request, cancellationToken);
            return result.ToHttp(item => Results.Ok(item.ToResponse()));
        })
        .WithName("UpdateItineraryItem");

        group.MapDelete("/itinerary/{itemId:guid}", async (
            Guid tripId,
            Guid itemId,
            ItineraryService itinerary,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await itinerary.DeleteAsync(tripId, itemId, caller.MemberId, cancellationToken);
            return result.ToHttp(item => Results.Ok(item.ToResponse()));
        })
        .WithName("DeleteItineraryItem");

        group.MapGet("/suggestions", async (
            Guid tripId,
            DateOnly? date,
            ItineraryService itinerary,
            CancellationToken cancellationToken) =>
        {
            var result = await itinerary.SuggestAsync(tripId, date, cancellationToken);
            return result.ToHttp(groups =>
                Results.Ok(groups.Select(g => g.ToResponse()).ToList()));
        })
        .WithName("GetSuggestions");
    }
}
