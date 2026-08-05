using Microsoft.EntityFrameworkCore;
using WeGo.Api.Errors;
using WeGo.Infrastructure.Persistence;

namespace WeGo.Api.Auth;

/// <summary>
/// The authorisation gate for every trip-scoped route (spec §5.7).
/// <para>
/// Two independent checks, both required. First the route's tripId must be one
/// the cookie actually holds a membership for — a session that lists trips A
/// and B can never address trip C. Then that member must still exist on that
/// trip, re-read from the database on every request, so the cookie alone is
/// never sufficient proof.
/// </para>
/// <para>
/// Trip existence is never probed separately: a member row can only exist for a
/// trip that exists, so a caller cannot use the status code to discover which
/// trip ids are real.
/// </para>
/// </summary>
public sealed class TripMemberFilter : IEndpointFilter
{
    public const string RouteParameterName = "tripId";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (!http.Request.RouteValues.TryGetValue(RouteParameterName, out var rawTripId)
            || !Guid.TryParse(rawTripId?.ToString(), out var routeTripId))
        {
            return Problems.From(Failure.NotFound("Trip not found."));
        }

        var options = http.RequestServices.GetRequiredService<AuthOptions>();
        var tokens = http.RequestServices.GetRequiredService<SessionTokenService>();

        if (!tokens.TryValidate(SessionCookie.Read(http, options), out var token))
        {
            return Problems.From(Failure.Unauthenticated(
                "Sign in by creating or joining a trip before calling this endpoint."));
        }

        if (!token.TryFind(routeTripId, out var claimedMemberId))
        {
            return Problems.From(Failure.Forbidden());
        }

        var db = http.RequestServices.GetRequiredService<WeGoDbContext>();
        var member = await db.Members
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == claimedMemberId && m.TripId == routeTripId)
            .ConfigureAwait(false);

        if (member is null)
        {
            return Problems.From(Failure.Forbidden());
        }

        http.Items[TripContext.HttpContextItemKey] =
            new TripContext(routeTripId, member.Id, member.DisplayName, member.Role);

        return await next(context).ConfigureAwait(false);
    }
}
