namespace WeGo.Domain.Entities;

/// <summary>
/// Join row for <c>Place.LikedByMemberIds</c> (spec §3). The primary key is the
/// composite (PlaceId, MemberId) rather than a surrogate Guid: that makes
/// "liking twice is a no-op" (spec §4) an invariant the database itself holds,
/// instead of a race the application has to win.
/// </summary>
public sealed class PlaceLike
{
    public Guid PlaceId { get; set; }

    public Guid MemberId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
