namespace WeGo.Domain.Common;

/// <summary>
/// Stable machine-readable codes surfaced in the ProblemDetails <c>code</c>
/// extension (spec §6). These are part of the public API contract — renaming
/// one is a breaking change.
/// </summary>
public static class ErrorCodes
{
    // Validation (422)
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string SuspiciousCoordinates = "SUSPICIOUS_COORDINATES";
    public const string DateOutOfRange = "DATE_OUT_OF_RANGE";
    public const string SharesSumMismatch = "SHARES_SUM_MISMATCH";

    // Conflict (409)
    public const string NameTaken = "NAME_TAKEN";
    public const string PlaceInUse = "PLACE_IN_USE";
    public const string ItemsOutOfRange = "ITEMS_OUT_OF_RANGE";
    public const string DuplicatePlaceOnDate = "DUPLICATE_PLACE_ON_DATE";
    public const string InvalidStatusTransition = "INVALID_STATUS_TRANSITION";
    public const string TripFull = "TRIP_FULL";

    /// <summary>
    /// This browser already holds as many trips as the session cookie can carry.
    /// A refusal rather than an eviction: dropping the oldest silently would
    /// lose a trip whose invite code is only ever displayed inside it.
    /// </summary>
    public const string DeviceTripLimit = "DEVICE_TRIP_LIMIT";

    /// <summary>
    /// This deployment only lets people with the shared code start a trip.
    /// Joining an existing one is unaffected — an invite link is enough.
    /// </summary>
    public const string InvalidAccessCode = "INVALID_ACCESS_CODE";

    // Auth (401/403)
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";

    // Misc
    public const string NotFound = "NOT_FOUND";
    public const string MalformedJson = "MALFORMED_JSON";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string RateLimited = "RATE_LIMITED";
    public const string WeatherUnavailable = "WEATHER_UNAVAILABLE";
    public const string GeocodingUnavailable = "GEOCODING_UNAVAILABLE";
    public const string LinkNotRecognised = "LINK_NOT_RECOGNISED";
    public const string InternalError = "INTERNAL_ERROR";
    public const string InviteCodeGenerationFailed = "INVITE_CODE_GENERATION_FAILED";
}

/// <summary>Per-field failure reasons reported inside a 422 body.</summary>
public static class FieldErrorCodes
{
    public const string Required = "REQUIRED";
    public const string TooShort = "TOO_SHORT";
    public const string TooLong = "TOO_LONG";
    public const string OutOfRange = "OUT_OF_RANGE";
    public const string Invalid = "INVALID";
    public const string Suspicious = "SUSPICIOUS";
}
