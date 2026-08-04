using WeGo.Domain.Common;
using WeGo.Domain.Entities;
using WeGo.Domain.Money;

namespace WeGo.Domain.Trips;

/// <summary>A fully validated set of trip field values, safe to write to an entity.</summary>
public sealed record TripDraft(
    string Name,
    string Destination,
    DateOnly StartDate,
    DateOnly EndDate,
    string TimeZoneId,
    string Currency,
    long? BudgetAmount);

/// <summary>
/// Pure trip validation (spec §6). No EF, no HTTP — every rule here is callable
/// from a unit test with plain values.
/// </summary>
public static class TripRules
{
    /// <summary>
    /// Validates the complete set of trip fields. Callers pass the already-merged
    /// values, so this covers both creation and update.
    /// </summary>
    public static (TripDraft? Draft, ValidationResult Result) Validate(
        string? name,
        string? destination,
        DateOnly? startDate,
        DateOnly? endDate,
        string? timeZoneId,
        string? currency,
        long? budgetAmount)
    {
        var result = new ValidationResult();

        var validName = StringInput.Required(result, "name", name, 1, TripDefaults.NameMaxLength);
        var validDestination = StringInput.Required(
            result, "destination", destination, 1, TripDefaults.DestinationMaxLength);

        if (startDate is null)
        {
            result.Add("startDate", FieldErrorCodes.Required, "'startDate' is required (format YYYY-MM-DD).");
        }

        if (endDate is null)
        {
            result.Add("endDate", FieldErrorCodes.Required, "'endDate' is required (format YYYY-MM-DD).");
        }

        ValidateDateSpan(result, startDate, endDate);

        var validTimeZone = ValidateTimeZone(result, timeZoneId);
        var validCurrency = ValidateCurrency(result, currency);

        if (budgetAmount is < 0)
        {
            result.Add("budgetAmount", FieldErrorCodes.OutOfRange, "'budgetAmount' cannot be negative.");
        }

        if (!result.IsValid
            || validName is null
            || validDestination is null
            || startDate is null
            || endDate is null
            || validTimeZone is null
            || validCurrency is null)
        {
            return (null, result);
        }

        return (
            new TripDraft(
                validName,
                validDestination,
                startDate.Value,
                endDate.Value,
                validTimeZone,
                validCurrency,
                budgetAmount),
            result);
    }

    private static void ValidateDateSpan(ValidationResult result, DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate is null || endDate is null)
        {
            return;
        }

        if (endDate.Value < startDate.Value)
        {
            result.Add("endDate", FieldErrorCodes.OutOfRange, "'endDate' must be on or after 'startDate'.");
            return;
        }

        // "Span <= 60 days" is read as the inclusive number of trip days, so a
        // trip may run from the 1st to the 60th but not the 61st. Recorded in
        // DECISIONS.md because the spec wording admits a one-day-different reading.
        var dayCount = endDate.Value.DayNumber - startDate.Value.DayNumber + 1;
        if (dayCount > TripDefaults.MaxSpanDays)
        {
            result.Add(
                "endDate",
                FieldErrorCodes.OutOfRange,
                $"A trip may span at most {TripDefaults.MaxSpanDays} days; this one spans {dayCount}.");
        }
    }

    private static string? ValidateTimeZone(ValidationResult result, string? timeZoneId)
    {
        var normalized = StringInput.Normalize(timeZoneId) ?? TripDefaults.TimeZoneId;

        if (!TryFindTimeZone(normalized))
        {
            result.Add(
                "timeZoneId",
                FieldErrorCodes.Invalid,
                $"'{normalized}' is not a recognised IANA time zone identifier.");
            return null;
        }

        return normalized;
    }

    public static bool TryFindTimeZone(string timeZoneId)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static string? ValidateCurrency(ValidationResult result, string? currency)
    {
        var normalized = StringInput.Normalize(currency) ?? TripDefaults.Currency;

        if (!CurrencyInfo.IsWellFormed(normalized))
        {
            result.Add(
                "currency",
                FieldErrorCodes.Invalid,
                "'currency' must be a three-letter ISO 4217 code, e.g. VND.");
            return null;
        }

        return CurrencyInfo.Normalize(normalized);
    }

    /// <summary>
    /// Spec §6: changing trip dates must not silently orphan scheduled items.
    /// Returns the ids of items that would fall outside the proposed range.
    /// </summary>
    public static IReadOnlyList<Guid> FindItemsOutsideRange(
        IEnumerable<ItineraryItem> items,
        DateOnly startDate,
        DateOnly endDate) =>
        items.Where(i => i.Date < startDate || i.Date > endDate)
             .OrderBy(i => i.Date)
             .ThenBy(i => i.CreatedAt)
             .Select(i => i.Id)
             .ToList();
}
