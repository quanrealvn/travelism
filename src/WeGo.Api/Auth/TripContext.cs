using WeGo.Domain;

namespace WeGo.Api.Auth;

/// <summary>
/// The verified caller for a trip-scoped request. Produced only by
/// <see cref="TripMemberFilter"/> after the database has confirmed the member
/// really belongs to the trip named in the route.
/// </summary>
public sealed record TripContext(Guid TripId, Guid MemberId, string DisplayName, MemberRole Role)
{
    public const string HttpContextItemKey = "WeGo.TripContext";
}

public static class TripContextAccessor
{
    /// <summary>
    /// Retrieves the context established by the endpoint filter. Throws if it is
    /// missing, because that can only mean an endpoint was registered outside
    /// the authorised group — a wiring bug, not a runtime condition.
    /// </summary>
    public static TripContext GetTripContext(this HttpContext context) =>
        context.Items[TripContext.HttpContextItemKey] as TripContext
        ?? throw new InvalidOperationException(
            "No TripContext on this request. The endpoint must be registered inside the trip-scoped group.");
}
