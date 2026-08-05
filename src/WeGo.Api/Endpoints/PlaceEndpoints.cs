using WeGo.Api.Auth;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Realtime;
using WeGo.Api.Services;
using WeGo.Domain.Entities;

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
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await places.CreateAsync(tripId, caller.MemberId, request, cancellationToken);

            return await result.BroadcastThenRespond(
                broadcaster, tripId, caller.MemberId,
                TripEvents.PlaceChanged, nameof(Place),
                place => place.Id,
                place => place.ToResponse(),
                place => Results.Created($"/trips/{tripId}/places/{place.Id}", place.ToResponse()),
                cancellationToken);
        })
        .WithName("CreatePlace");

        // Registered before the {placeId:guid} routes: "search" is a literal
        // segment, which routing prefers over a parameter, and it cannot parse
        // as a Guid in any case.
        group.MapGet("/search", async (
            Guid tripId,
            string? q,
            int? limit,
            GeocodingService geocoding,
            CancellationToken cancellationToken) =>
        {
            var result = await geocoding.SearchAsync(tripId, q, limit, cancellationToken);
            return result.ToOk();
        })
        .WithName("SearchPlaces")
        // Calls Nominatim, which enforces its policy by banning the caller.
        .RequireRateLimiting(RateLimitPolicies.Geocode);

        group.MapPost("/resolve-link", async (
            Guid tripId,
            ResolveLinkRequest request,
            GeocodingService geocoding,
            CancellationToken cancellationToken) =>
        {
            var result = await geocoding.ResolveLinkAsync(tripId, request.Url, cancellationToken);
            return result.ToOk();
        })
        .WithName("ResolvePlaceLink")
        .RequireRateLimiting(RateLimitPolicies.Geocode);

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
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await places.UpdateAsync(tripId, placeId, caller.MemberId, request, cancellationToken);

            return await result.BroadcastThenRespond(
                broadcaster, tripId, caller.MemberId,
                TripEvents.PlaceChanged, nameof(Place),
                place => place.Id,
                place => place.ToResponse(),
                place => Results.Ok(place.ToResponse()),
                cancellationToken);
        })
        .WithName("UpdatePlace");

        group.MapPost("/{placeId:guid}/like", async (
            Guid tripId,
            Guid placeId,
            PlaceService places,
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await places.LikeAsync(tripId, placeId, caller.MemberId, cancellationToken);

            return await result.BroadcastThenRespond(
                broadcaster, tripId, caller.MemberId,
                TripEvents.PlaceChanged, nameof(Place),
                place => place.Id,
                place => place.ToResponse(),
                place => Results.Ok(place.ToResponse()),
                cancellationToken);
        })
        .WithName("LikePlace");

        group.MapDelete("/{placeId:guid}/like", async (
            Guid tripId,
            Guid placeId,
            PlaceService places,
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await places.UnlikeAsync(tripId, placeId, caller.MemberId, cancellationToken);

            return await result.BroadcastThenRespond(
                broadcaster, tripId, caller.MemberId,
                TripEvents.PlaceChanged, nameof(Place),
                place => place.Id,
                place => place.ToResponse(),
                place => Results.Ok(place.ToResponse()),
                cancellationToken);
        })
        .WithName("UnlikePlace");

        group.MapPost("/{placeId:guid}/status", async (
            Guid tripId,
            Guid placeId,
            ChangePlaceStatusRequest request,
            PlaceService places,
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await places.ChangeStatusAsync(
                tripId, placeId, caller.MemberId, request.Status, request.SkipReason, cancellationToken);

            return await result.BroadcastThenRespond(
                broadcaster, tripId, caller.MemberId,
                TripEvents.PlaceChanged, nameof(Place),
                place => place.Id,
                place => place.ToResponse(),
                place => Results.Ok(place.ToResponse()),
                cancellationToken);
        })
        .WithName("ChangePlaceStatus");

        group.MapDelete("/{placeId:guid}", async (
            Guid tripId,
            Guid placeId,
            bool? force,
            PlaceService places,
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await places.DeleteAsync(
                tripId, placeId, caller.MemberId, force == true, cancellationToken);

            return await result.BroadcastThenRespond(
                broadcaster, tripId, caller.MemberId,
                TripEvents.PlaceDeleted, nameof(Place),
                place => place.Id,
                place => place.ToResponse(),
                place => Results.Ok(place.ToResponse()),
                cancellationToken);
        })
        .WithName("DeletePlace");
    }
}
