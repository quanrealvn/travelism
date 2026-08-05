using Microsoft.EntityFrameworkCore;
using WeGo.Api.Auth;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Services;
using WeGo.Infrastructure.Persistence;

namespace WeGo.Api.Endpoints;

public static class WeatherEndpoints
{
    public static void MapWeatherEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/trips/{tripId:guid}").AddEndpointFilter<TripMemberFilter>();

        group.MapGet("/weather", async (
            Guid tripId,
            WeatherService weather,
            CancellationToken cancellationToken) =>
        {
            var result = await weather.GetAsync(tripId, cancellationToken);

            // Spec §5.5: nothing to forecast is 204, not an empty 200 — the
            // client must be able to tell "no location yet" from "no rain".
            return result.ToHttp(forecast =>
                forecast is null ? Results.NoContent() : Results.Ok(forecast));
        })
        .WithName("GetWeather");

        group.MapGet("/activity", async (
            Guid tripId,
            int? limit,
            WeGoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var take = Math.Clamp(limit ?? 50, 1, 200);

            var entries = await db.ActivityLogs
                .AsNoTracking()
                .Where(a => a.TripId == tripId)
                .OrderByDescending(a => a.At)
                .Take(take)
                .Select(a => new ActivityResponse(
                    a.Id, a.MemberId, a.Action.ToString(), a.EntityType, a.EntityId, a.SummaryText, a.At))
                .ToListAsync(cancellationToken);

            return Results.Ok(entries);
        })
        .WithName("GetActivity");
    }
}
