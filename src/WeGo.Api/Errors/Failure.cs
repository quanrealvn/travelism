using WeGo.Domain.Common;

namespace WeGo.Api.Errors;

/// <summary>
/// A rejected operation, carrying everything needed to render RFC 7807
/// ProblemDetails with a stable machine-readable <c>code</c> (spec §6).
/// Services return this instead of throwing, so the endpoint stays a thin
/// parse → validate → call → map pipeline.
/// </summary>
public sealed record Failure(
    int Status,
    string Code,
    string Detail,
    IReadOnlyList<ValidationError>? Errors = null,
    IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public static Failure Validation(ValidationResult result, string detail = "The request body failed validation.") =>
        new(StatusCodes.Status422UnprocessableEntity, result.TopLevelCode, detail, result.Errors);

    public static Failure NotFound(string detail = "The requested resource does not exist.") =>
        new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, detail);

    public static Failure Forbidden(string detail = "You are not a member of this trip.") =>
        new(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, detail);

    public static Failure Unauthenticated(string detail = "No valid session for this request.") =>
        new(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated, detail);

    public static Failure Conflict(
        string code,
        string detail,
        IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(StatusCodes.Status409Conflict, code, detail, Errors: null, Extensions: extensions);

    public static Failure Unprocessable(
        string code,
        string detail,
        IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(StatusCodes.Status422UnprocessableEntity, code, detail, Errors: null, Extensions: extensions);
}
