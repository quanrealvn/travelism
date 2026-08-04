using WeGo.Api.Auth;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Services;

namespace WeGo.Api.Endpoints;

public static class PlaceEndpoints
{
    public static void MapPlaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/trips/{tripId:guid}/places").AddEndpointFilter<TripMemberFilter>();

        group.MapGet("/", async (
            Guid tripId,
            bool? includeDeleted,
            PlaceService places,
            CancellationToken cancellationToken) =>
        {
            var result = await places.ListAsync(tripId, includeDeleted == true, cancellationToken);
            return Results.Ok(result.Select(p => p.ToResponse()).ToList());
        })
        .WithName("ListPlaces");

        group.MapPost("/", async (
            Guid tripId,
            CreatePlaceRequest request,
            PlaceService places,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await places.CreateAsync(tripId, caller.MemberId, request, cancellationToken);
            return result.ToHttp(place =>
                Results.Created($"/trips/{tripId}/places/{place.Id}", place.ToResponse()));
        })
        .WithName("CreatePlace");

        group.MapGet("/{placeId:guid}", async (
            Guid tripId,
            Guid placeId,
            PlaceService places,
            CancellationToken cancellationToken) =>
        {
            var result = await places.GetAsync(tripId, placeId, cancellationToken);
            return result.ToHttp(place => Results.Ok(place.ToResponse()));
        })
        .WithName("GetPlace");

        group.MapPatch("/{placeId:guid}", async (
            Guid tripId,
            Guid placeId,
            UpdatePlaceRequest request,
            PlaceService places,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await places.UpdateAsync(tripId, placeId, caller.MemberId, request, cancellationToken);
            return result.ToHttp(place => Results.Ok(place.ToResponse()));
        })
        .WithName("UpdatePlace");

        group.MapDelete("/{placeId:guid}", async (
            Guid tripId,
            Guid placeId,
            bool? force,
            PlaceService places,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await places.DeleteAsync(
                tripId, placeId, caller.MemberId, force == true, cancellationToken);
            return result.ToHttp(place => Results.Ok(place.ToResponse()));
        })
        .WithName("DeletePlace");
    }
}
