using WeGo.Domain.Common;
using WeGo.Domain.Entities;

namespace WeGo.Domain.Members;

public static class MemberRules
{
    /// <param name="fieldName">
    /// The JSON field the value arrived in. Trip creation sends it as
    /// <c>ownerDisplayName</c> and joining sends it as <c>displayName</c>; the
    /// 422 has to name the field the client actually wrote.
    /// </param>
    public static (string? DisplayName, ValidationResult Result) ValidateDisplayName(
        string? displayName,
        string fieldName = "displayName")
    {
        var result = new ValidationResult();
        var valid = StringInput.Required(
            result,
            fieldName,
            displayName,
            MemberDefaults.DisplayNameMinLength,
            MemberDefaults.DisplayNameMaxLength);

        return (valid, result);
    }

    /// <summary>
    /// Spec §5.7: display names are unique within a trip, case-insensitively.
    /// Compared with <see cref="StringComparison.OrdinalIgnoreCase"/> so the check
    /// does not vary with the server's current culture.
    /// </summary>
    public static bool IsNameTaken(IEnumerable<Member> existing, string candidate) =>
        existing.Any(m => string.Equals(m.DisplayName, candidate, StringComparison.OrdinalIgnoreCase));

    /// <summary>Spec §3: hard cap of 10 members per trip.</summary>
    public static bool IsTripFull(int currentMemberCount) => currentMemberCount >= TripDefaults.MaxMembers;
}
