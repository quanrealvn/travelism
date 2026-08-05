using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WeGo.Api.Auth;
using WeGo.Infrastructure.Persistence;

namespace WeGo.Api.Realtime;

/// <summary>
/// One group per trip (spec §5.8). Server-to-client only: the hub exposes no
/// methods a client can invoke, so it cannot become a second, unvalidated way
/// to mutate a trip. Every change still goes through the HTTP endpoints.
/// </summary>
public sealed class TripHub(
    AuthOptions authOptions,
    SessionTokenService tokens,
    WeGoDbContext db) : Hub
{
    public static string GroupName(Guid tripId) => $"trip:{tripId}";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var tripId = ResolveTripId(http);

        // Same cookie, same membership check as every HTTP route (spec §5.8).
        // A socket must not be an easier door than a request.
        //
        // Throwing rather than calling Context.Abort(): abort tears the
        // connection down *after* the client's StartAsync has already returned
        // successfully, so a refused client would believe it was subscribed and
        // sit waiting for events that never come. Throwing fails the start.
        Guid claimedMemberId = default;
        if (http is null
            || tripId is null
            || !tokens.TryValidate(SessionCookie.Read(http, authOptions), out var token)
            || !token.TryFind(tripId.Value, out claimedMemberId))
        {
            throw new HubException("Not authorised for this trip.");
        }

        var isMember = await db.Members
            .AsNoTracking()
            .AnyAsync(m => m.Id == claimedMemberId && m.TripId == tripId.Value)
            .ConfigureAwait(false);

        if (!isMember)
        {
            throw new HubException("Not authorised for this trip.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tripId.Value)).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The trip is named in the query string because a WebSocket handshake
    /// cannot carry a request body, and browsers cannot set headers on it.
    /// It is only ever a claim: the cookie decides whether it is honoured.
    /// </summary>
    private static Guid? ResolveTripId(HttpContext? http)
    {
        var raw = http?.Request.Query["tripId"].ToString();
        return Guid.TryParse(raw, out var tripId) ? tripId : null;
    }
}
