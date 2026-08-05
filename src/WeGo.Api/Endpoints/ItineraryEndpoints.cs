using WeGo.Api.Auth;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Realtime;
using WeGo.Api.Services;
using WeGo.Domain.Entities;

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
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await itinerary.CreateAsync(tripId, caller.MemberId, request, cancellationToken);

            return await result.BroadcastThenRespond(
                broadcaster, tripId, caller.MemberId,
                TripEvents.ItineraryChanged, nameof(ItineraryItem),
                item => item.Id,
                item => item.ToResponse(),
                item => Results.Created($"/trips/{tripId}/itinerary/{item.Id}", item.ToResponse()),
                cancellationToken);
        })
        .WithName("CreateItineraryItem");

        group.MapPatch("/itinerary/{itemId:guid}", async (
            Guid tripId,
            Guid itemId,
            UpdateItineraryItemRequest request,
            ItineraryService itinerary,
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await itinerary.UpdateAsync(
                tripId, itemId, caller.MemberId, request, cancellationToken);

            return await result.BroadcastThenRespond(
                broadcaster, tripId, caller.MemberId,
                TripEvents.ItineraryChanged, nameof(ItineraryItem),
                item => item.Id,
                item => item.ToResponse(),
                item => Results.Ok(item.ToResponse()),
                cancellationToken);
        })
        .WithName("UpdateItineraryItem");

        group.MapDelete("/itinerary/{itemId:guid}", async (
            Guid tripId,
            Guid itemId,
            ItineraryService itinerary,
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await itinerary.DeleteAsync(tripId, itemId, caller.MemberId, cancellationToken);

            return await result.BroadcastThenRespond(
                broadcaster, tripId, caller.MemberId,
                TripEvents.ItineraryChanged, nameof(ItineraryItem),
                item => item.Id,
                item => item.ToResponse(),
                item => Results.Ok(item.ToResponse()),
                cancellationToken);
        })
        .WithName("DeleteItineraryItem");

        group.MapGet("/itinerary/feasibility", async (
            Guid tripId,
            DateOnly? date,
            ItineraryService itinerary,
            CancellationToken cancellationToken) =>
        {
            var result = await itinerary.FeasibilityAsync(tripId, date, cancellationToken);
            return result.ToHttp(findings => Results.Ok(new FeasibilityResponse(
                findings
                    .Select(f => new FeasibilityFindingResponse(
                        f.ItineraryItemId,
                        f.Level.ToString().ToLowerInvariant(),
                        f.Code,
                        f.Data))
                    .ToList())));
        })
        .WithName("GetFeasibility");

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
