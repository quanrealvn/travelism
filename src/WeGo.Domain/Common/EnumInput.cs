namespace WeGo.Domain.Common;

/// <summary>
/// Enums arrive over the wire as strings and are parsed here rather than by
/// System.Text.Json. That is deliberate: the JSON converter throws on an unknown
/// value, which surfaces as a framework 400 with a non-conforming body, whereas
/// spec §6 requires ProblemDetails with a stable code for every rejection.
/// Parsing here yields a normal per-field 422 instead.
/// </summary>
public static class EnumInput
{
    /// <summary>Parses a required enum value. Returns null (and records an error) on failure.</summary>
    public static TEnum? Required<TEnum>(ValidationResult result, string field, string? value)
        where TEnum : struct, Enum
    {
        var normalized = StringInput.Normalize(value);
        if (normalized is null)
        {
            result.Add(field, FieldErrorCodes.Required, $"'{field}' is required. Allowed: {Allowed<TEnum>()}.");
            return null;
        }

        return Parse<TEnum>(result, field, normalized);
    }

    /// <summary>Parses an optional enum value. Absent/blank yields null with no error.</summary>
    public static TEnum? Optional<TEnum>(ValidationResult result, string field, string? value)
        where TEnum : struct, Enum
    {
        var normalized = StringInput.Normalize(value);
        return normalized is null ? null : Parse<TEnum>(result, field, normalized);
    }

    private static TEnum? Parse<TEnum>(ValidationResult result, string field, string normalized)
        where TEnum : struct, Enum
    {
        // Matching against the declared names (rather than Enum.TryParse) rejects
        // numeric strings such as "7", which would otherwise parse into an
        // undefined enum member and corrupt the row.
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse<TEnum>(name);
            }
        }

        result.Add(field, FieldErrorCodes.Invalid, $"'{field}' must be one of: {Allowed<TEnum>()}.");
        return null;
    }

    private static string Allowed<TEnum>() where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetNames<TEnum>());
}
