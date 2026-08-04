using WeGo.Domain.Common;
using WeGo.Domain.Entities;

namespace WeGo.Domain.Places;

/// <summary>A fully validated set of place field values, safe to write to an entity.</summary>
public sealed record PlaceDraft(
    string Name,
    double Lat,
    double Lng,
    PlaceCategory Category,
    TimeSlots TimeSlots,
    int EstimatedDurationMinutes,
    long? EstimatedCost,
    string? OpenHoursText);

/// <summary>Pure place validation (spec §3 field bounds + §6 coordinate rules).</summary>
public static class PlaceRules
{
    public static (PlaceDraft? Draft, ValidationResult Result) Validate(
        string? name,
        double? lat,
        double? lng,
        string? category,
        IReadOnlyList<string?>? timeSlots,
        int? estimatedDurationMinutes,
        long? estimatedCost,
        string? openHoursText)
    {
        var result = new ValidationResult();

        var validName = StringInput.Required(result, "name", name, 1, PlaceDefaults.NameMaxLength);
        var coordinates = ValidateCoordinates(result, lat, lng);
        var validCategory = EnumInput.Required<PlaceCategory>(result, "category", category);
        var validSlots = ValidateTimeSlots(result, timeSlots);
        var validDuration = ValidateDuration(result, estimatedDurationMinutes);

        if (estimatedCost is < 0)
        {
            result.Add("estimatedCost", FieldErrorCodes.OutOfRange, "'estimatedCost' cannot be negative.");
        }

        var validOpenHours = StringInput.Optional(
            result, "openHoursText", openHoursText, PlaceDefaults.OpenHoursTextMaxLength);

        if (!result.IsValid
            || validName is null
            || coordinates is null
            || validCategory is null
            || validSlots is null
            || validDuration is null)
        {
            return (null, result);
        }

        return (
            new PlaceDraft(
                validName,
                coordinates.Value.Lat,
                coordinates.Value.Lng,
                validCategory.Value,
                validSlots.Value,
                validDuration.Value,
                estimatedCost,
                validOpenHours),
            result);
    }

    private static (double Lat, double Lng)? ValidateCoordinates(
        ValidationResult result,
        double? lat,
        double? lng)
    {
        var ok = true;

        if (lat is null)
        {
            result.Add("lat", FieldErrorCodes.Required, "'lat' is required.");
            ok = false;
        }
        else if (double.IsNaN(lat.Value) || double.IsInfinity(lat.Value) || lat.Value is < -90 or > 90)
        {
            result.Add("lat", FieldErrorCodes.OutOfRange, "'lat' must be between -90 and 90.");
            ok = false;
        }

        if (lng is null)
        {
            result.Add("lng", FieldErrorCodes.Required, "'lng' is required.");
            ok = false;
        }
        else if (double.IsNaN(lng.Value) || double.IsInfinity(lng.Value) || lng.Value is < -180 or > 180)
        {
            result.Add("lng", FieldErrorCodes.OutOfRange, "'lng' must be between -180 and 180.");
            ok = false;
        }

        // Spec §6: (0,0) is in the Gulf of Guinea and is almost always an
        // uninitialised client field, so it is rejected on its own code rather
        // than quietly stored as a real location.
        if (ok && lat is 0 && lng is 0)
        {
            result.Add(
                "lat",
                FieldErrorCodes.Suspicious,
                "Coordinates (0, 0) are rejected as a probable client bug (Null Island).");
            return null;
        }

        return ok ? (lat!.Value, lng!.Value) : null;
    }

    private static TimeSlots? ValidateTimeSlots(ValidationResult result, IReadOnlyList<string?>? timeSlots)
    {
        if (timeSlots is null || timeSlots.Count == 0)
        {
            result.Add(
                "timeSlots",
                FieldErrorCodes.Required,
                "'timeSlots' must contain at least one of: Morning, Noon, Afternoon, Evening.");
            return null;
        }

        var combined = TimeSlots.None;
        var hadError = false;

        foreach (var slot in timeSlots)
        {
            var normalized = StringInput.Normalize(slot);

            // 'None' is a representational zero, not a slot a user can pick;
            // accepting it would let a place claim "at least one slot" while
            // matching no time of day at all.
            if (normalized is null
                || string.Equals(normalized, nameof(TimeSlots.None), StringComparison.OrdinalIgnoreCase))
            {
                result.Add(
                    "timeSlots",
                    FieldErrorCodes.Invalid,
                    "Each 'timeSlots' entry must be one of: Morning, Noon, Afternoon, Evening.");
                hadError = true;
                continue;
            }

            var parsed = EnumInput.Required<TimeSlots>(result, "timeSlots", normalized);
            if (parsed is null)
            {
                hadError = true;
                continue;
            }

            combined |= parsed.Value;
        }

        return hadError || combined == TimeSlots.None ? null : combined;
    }

    private static int? ValidateDuration(ValidationResult result, int? estimatedDurationMinutes)
    {
        if (estimatedDurationMinutes is null)
        {
            result.Add("estimatedDurationMinutes", FieldErrorCodes.Required, "'estimatedDurationMinutes' is required.");
            return null;
        }

        if (estimatedDurationMinutes.Value < PlaceDefaults.MinDurationMinutes
            || estimatedDurationMinutes.Value > PlaceDefaults.MaxDurationMinutes)
        {
            result.Add(
                "estimatedDurationMinutes",
                FieldErrorCodes.OutOfRange,
                $"'estimatedDurationMinutes' must be between {PlaceDefaults.MinDurationMinutes} "
                    + $"and {PlaceDefaults.MaxDurationMinutes}.");
            return null;
        }

        return estimatedDurationMinutes.Value;
    }

    /// <summary>
    /// Coordinates moving invalidates any cached travel time touching this place
    /// in either direction (spec §7.4).
    /// </summary>
    public static bool CoordinatesChanged(Place place, double newLat, double newLng) =>
        // Exact comparison is correct here: these values round-trip through JSON
        // and SQLite unchanged, and a "close enough" epsilon would silently keep
        // a stale route after a small but real move.
        place.Lat != newLat || place.Lng != newLng;
}
