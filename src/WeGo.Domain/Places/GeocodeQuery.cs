using WeGo.Domain.Common;

namespace WeGo.Domain.Places;

/// <summary>
/// Validation for a place-name lookup. Pure, so the bounds are testable without
/// a host and without touching the upstream geocoder.
/// </summary>
public static class GeocodeQuery
{
    /// <summary>
    /// One or two characters match almost everything, so the upstream request
    /// would be expensive and the results useless. The floor is a courtesy to
    /// the shared OpenStreetMap service as much as to the user.
    /// </summary>
    public const int MinLength = 2;

    public const int MaxLength = 200;

    public const int DefaultLimit = 8;

    public const int MaxLimit = 20;

    public static (string? Query, ValidationResult Result) Validate(string? query)
    {
        var result = new ValidationResult();
        var valid = StringInput.Required(result, "q", query, MinLength, MaxLength);
        return (valid, result);
    }

    /// <summary>Clamps a caller-supplied result count into a sane range.</summary>
    public static int ClampLimit(int? limit) =>
        limit is null ? DefaultLimit : Math.Clamp(limit.Value, 1, MaxLimit);
}
