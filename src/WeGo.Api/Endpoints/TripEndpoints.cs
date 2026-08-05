using WeGo.Api.Auth;
using WeGo.Api.Common;
using WeGo.Api.Contracts;
using WeGo.Api.Errors;
using WeGo.Api.Realtime;
using WeGo.Api.Services;
using WeGo.Domain.Common;
using WeGo.Domain.Entities;

namespace WeGo.Api.Endpoints;

/// <summary>
/// Endpoints are thin (spec §8): bind → delegate → map. No query logic and no
/// business rules live here.
/// </summary>
public static class TripEndpoints
{
    public static void MapTripEndpoints(this IEndpointRouteBuilder app)
    {
        MapPublicRoutes(app);
        MapTripScopedRoutes(app);
    }

    private static void MapPublicRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/trips", async (
            CreateTripRequest request,
            TripService trips,
            AuthOptions authOptions,
            SessionTokenService tokens,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var result = await trips.CreateAsync(request, cancellationToken);
            return result.ToHttp(created =>
            {
                IssueSession(http, authOptions, tokens, created);
                return Results.Created($"/trips/{created.Trip.Id}", ToSessionResponse(created));
            });
        })
        .WithName("CreateTrip");

        app.MapPost("/trips/join", async (
            JoinTripRequest request,
            TripService trips,
            AuthOptions authOptions,
            SessionTokenService tokens,
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var result = await trips.JoinAsync(request, cancellationToken);

            if (result.IsSuccess)
            {
                var joined = result.Value!;
                await broadcaster.BroadcastAsync(
                    joined.Trip.Id,
                    TripEvents.MemberJoined,
                    nameof(Member),
                    joined.Member.Id,
                    joined.Member.ToResponse(),
                    joined.Member.Id,
                    cancellationToken);
            }

            return result.ToHttp(joined =>
            {
                IssueSession(http, authOptions, tokens, joined);
                return Results.Ok(ToSessionResponse(joined));
            });
        })
        .WithName("JoinTrip")
        .RequireRateLimiting(RateLimitPolicies.Join);

        app.MapGet("/session", (
            HttpContext http,
            AuthOptions authOptions,
            SessionTokenService tokens) =>
        {
            if (!tokens.TryValidate(SessionCookie.Read(http, authOptions), out var token))
            {
                return Problems.From(Failure.Unauthenticated("No session cookie present."));
            }

            // The first membership is the most recently created or joined trip,
            // which is the one to open when the client has no other preference.
            var current = token.Memberships[0];
            return Results.Ok(new
            {
                tripId = current.TripId,
                memberId = current.MemberId,
                memberships = token.Memberships
                    .Select(m => new { tripId = m.TripId, memberId = m.MemberId })
                    .ToArray(),
            });
        })
        .WithName("GetSession");

        // Every trip this browser holds, for the switcher. Trips the caller has
        // since been removed from are dropped rather than reported: the cookie
        // is a claim, and the member rows are the authority.
        app.MapGet("/trips/mine", async (
            HttpContext http,
            AuthOptions authOptions,
            SessionTokenService tokens,
            TripService trips,
            CancellationToken cancellationToken) =>
        {
            if (!tokens.TryValidate(SessionCookie.Read(http, authOptions), out var token))
            {
                return Problems.From(Failure.Unauthenticated("No session cookie present."));
            }

            var summaries = await trips.ListAsync(token.Memberships, cancellationToken);
            return Results.Ok(summaries);
        })
        .WithName("ListMyTrips");

        // Forget a trip on this device. The trip itself is untouched — this is
        // not "leave the trip", and rejoining with the invite code restores it.
        app.MapDelete("/session/trips/{tripId:guid}", (
            Guid tripId,
            HttpContext http,
            AuthOptions authOptions,
            SessionTokenService tokens) =>
        {
            if (!tokens.TryValidate(SessionCookie.Read(http, authOptions), out var token))
            {
                return Problems.From(Failure.Unauthenticated("No session cookie present."));
            }

            var next = token.Without(tripId);
            if (next.Memberships.Count == 0)
            {
                SessionCookie.Clear(http, authOptions);
            }
            else
            {
                SessionCookie.Write(http, authOptions, tokens.Issue(next));
            }

            return Results.NoContent();
        })
        .WithName("ForgetTrip");
    }

    private static void MapTripScopedRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/trips/{tripId:guid}").AddEndpointFilter<TripMemberFilter>();

        group.MapGet("/", async (
            Guid tripId,
            TripService trips,
            CancellationToken cancellationToken) =>
        {
            var result = await trips.GetAsync(tripId, cancellationToken);
            return result.ToHttp(t => Results.Ok(t.Trip.ToResponse(t.Members)));
        })
        .WithName("GetTrip");

        group.MapPatch("/", async (
            Guid tripId,
            UpdateTripRequest request,
            TripService trips,
            ITripBroadcaster broadcaster,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var caller = http.GetTripContext();
            var result = await trips.UpdateAsync(tripId, caller.MemberId, request, cancellationToken);

            return await result.BroadcastThenRespond(
                broadcaster, tripId, caller.MemberId,
                TripEvents.TripChanged, nameof(Trip),
                t => t.Trip.Id,
                t => t.Trip.ToResponse(t.Members),
                t => Results.Ok(t.Trip.ToResponse(t.Members)),
                cancellationToken);
        })
        .WithName("UpdateTrip");

        group.MapGet("/snapshot", async (
            Guid tripId,
            SnapshotService snapshots,
            CancellationToken cancellationToken) =>
        {
            // Spec §5.8: everything a reconnecting client needs, in one call.
            var snapshot = await snapshots.GetAsync(tripId, cancellationToken);
            return Results.Ok(snapshot);
        })
        .WithName("GetSnapshot");

        group.MapGet("/members", async (
            Guid tripId,
            TripService trips,
            CancellationToken cancellationToken) =>
        {
            var result = await trips.GetAsync(tripId, cancellationToken);
            return result.ToHttp(t => Results.Ok(
                t.Members.OrderBy(m => m.CreatedAt).Select(m => m.ToResponse()).ToList()));
        })
        .WithName("ListMembers");

        // Spec §5.6: trip deletion is out of scope for v1 and answers 405 rather
        // than 404, so the route is visibly reserved instead of looking absent.
        group.MapDelete("/", () => Problems.From(new Failure(
            StatusCodes.Status405MethodNotAllowed,
            ErrorCodes.MethodNotAllowed,
            "Deleting a trip is not supported in v1.")))
        .WithName("DeleteTrip");
    }

    /// <summary>
    /// Adds the new trip to whatever this browser already holds, rather than
    /// replacing it. Creating a second trip used to sign the browser out of the
    /// first, which quietly cost people last month's plan.
    /// </summary>
    private static void IssueSession(
        HttpContext http,
        AuthOptions authOptions,
        SessionTokenService tokens,
        TripWithSession created)
    {
        tokens.TryValidate(SessionCookie.Read(http, authOptions), out var existing);

        var next = existing.With(new TripMembership(created.Trip.Id, created.Member.Id));
        SessionCookie.Write(http, authOptions, tokens.Issue(next));
    }

    private static TripSessionResponse ToSessionResponse(TripWithSession created) =>
        new(
            created.Trip.ToResponse(created.Members),
            new SessionResponse(
                created.Trip.Id,
                created.Member.Id,
                created.Member.DisplayName,
                created.Member.Role.ToString()));
}
