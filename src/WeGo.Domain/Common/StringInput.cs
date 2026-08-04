namespace WeGo.Domain.Common;

/// <summary>
/// Spec §6: "All string inputs trimmed; reject strings that are whitespace-only
/// where min length ≥ 1." Centralised so every endpoint trims identically.
/// </summary>
public static class StringInput
{
    /// <summary>Trims and collapses empty/whitespace-only to <c>null</c>.</summary>
    public static string? Normalize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// Validates a required string: present, non-whitespace, within length bounds.
    /// Returns the normalized value, or <c>null</c> when a problem was recorded.
    /// </summary>
    public static string? Required(
        ValidationResult result,
        string field,
        string? value,
        int minLength,
        int maxLength)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            result.Add(field, FieldErrorCodes.Required, $"'{field}' is required and cannot be blank.");
            return null;
        }

        if (normalized.Length < minLength)
        {
            result.Add(field, FieldErrorCodes.TooShort, $"'{field}' must be at least {minLength} characters.");
            return null;
        }

        if (normalized.Length > maxLength)
        {
            result.Add(field, FieldErrorCodes.TooLong, $"'{field}' must be at most {maxLength} characters.");
            return null;
        }

        return normalized;
    }

    /// <summary>
    /// Validates an optional string. Whitespace-only becomes <c>null</c> (cleared)
    /// rather than an error, since there is no minimum to violate.
    /// </summary>
    public static string? Optional(
        ValidationResult result,
        string field,
        string? value,
        int maxLength)
    {
        var normalized = Normalize(value);
        if (normalized is not null && normalized.Length > maxLength)
        {
            result.Add(field, FieldErrorCodes.TooLong, $"'{field}' must be at most {maxLength} characters.");
            return null;
        }

        return normalized;
    }
}
