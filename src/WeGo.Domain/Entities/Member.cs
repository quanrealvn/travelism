using WeGo.Domain.Common;

namespace WeGo.Domain.Entities;

public sealed class Member : Entity
{
    public Guid TripId { get; set; }

    public required string DisplayName { get; set; }

    public MemberRole Role { get; set; } = MemberRole.Editor;
}

public static class MemberDefaults
{
    public const int DisplayNameMinLength = 1;
    public const int DisplayNameMaxLength = 40;
}
