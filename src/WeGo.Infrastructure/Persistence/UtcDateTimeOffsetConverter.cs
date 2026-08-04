using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WeGo.Infrastructure.Persistence;

/// <summary>
/// Stores every instant as a fixed-width ISO-8601 UTC string.
/// <para>
/// SQLite has no native <see cref="DateTimeOffset"/>, and EF's default mapping
/// cannot be used in ORDER BY — the provider rejects the query outright. Writing
/// the value normalised to UTC in round-trip format gives a representation that
/// is fixed width and therefore sorts lexicographically in exactly the order the
/// instants occurred.
/// </para>
/// <para>
/// Normalising on write also enforces the spec §3 rule that all stored
/// timestamps are UTC, at the one place every timestamp must pass through.
/// </para>
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, string>
{
    /// <summary>Round-trip format, e.g. <c>2026-03-01T08:30:00.0000000+00:00</c>.</summary>
    private const string Format = "O";

    public UtcDateTimeOffsetConverter()
        : base(
            value => value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture),
            value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
    {
    }
}

public sealed class NullableUtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset?, string?>
{
    private const string Format = "O";

    public NullableUtcDateTimeOffsetConverter()
        : base(
            value => value == null
                ? null
                : value.Value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture),
            value => value == null
                ? null
                : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
    {
    }
}
