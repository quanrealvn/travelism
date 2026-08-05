using Microsoft.AspNetCore.SignalR;
using WeGo.Domain.Abstractions;

namespace WeGo.Api.Realtime;

/// <summary>The events a client may receive (spec §5.8).</summary>
public static class TripEvents
{
    public const string PlaceChanged = "PlaceChanged";
    public const string PlaceDeleted = "PlaceDeleted";
    public const string ItineraryChanged = "ItineraryChanged";
    public const string ExpenseChanged = "ExpenseChanged";
    public const string TripChanged = "TripChanged";
    public const string MemberJoined = "MemberJoined";
}

/// <summary>The broadcast payload shape from spec §5.8.</summary>
public sealed record TripEvent(
    string Event,
    string EntityType,
    Guid EntityId,
    object? Payload,
    Guid ByMemberId,
    DateTimeOffset At);

public interface ITripBroadcaster
{
    /// <summary>
    /// Announces a change to everyone watching the trip.
    /// <para>
    /// Call this only after SaveChanges has returned (spec §5.8). Broadcasting
    /// first would tell other clients about a change that a failed transaction
    /// then rolled back, and they have no way to learn it was undone.
    /// </para>
    /// </summary>
    Task BroadcastAsync(
        Guid tripId,
        string eventName,
        string entityType,
        Guid entityId,
        object? payload,
        Guid byMemberId,
        CancellationToken cancellationToken = default);
}

public sealed class TripBroadcaster(IHubContext<TripHub> hub, IClock clock) : ITripBroadcaster
{
    public Task BroadcastAsync(
        Guid tripId,
        string eventName,
        string entityType,
        Guid entityId,
        object? payload,
        Guid byMemberId,
        CancellationToken cancellationToken = default)
    {
        var message = new TripEvent(eventName, entityType, entityId, payload, byMemberId, clock.UtcNow);

        return hub.Clients
            .Group(TripHub.GroupName(tripId))
            .SendAsync("tripEvent", message, cancellationToken);
    }
}
