namespace WeGo.Domain.Common;

/// <summary>One field-level problem, reported in the 422 body.</summary>
/// <param name="Field">camelCase JSON path of the offending field.</param>
/// <param name="Code">Stable reason code, see <see cref="FieldErrorCodes"/>.</param>
/// <param name="Message">Human-readable explanation. Never contains user secrets.</param>
public sealed record ValidationError(string Field, string Code, string Message);

/// <summary>
/// Accumulates every field problem in one pass so the client gets the complete
/// list rather than one error at a time (spec §7.14: "422 with per-field errors").
/// Pure — no framework types, so all validation is unit-testable without a host.
/// </summary>
public sealed class ValidationResult
{
    private readonly List<ValidationError> _errors = [];

    public IReadOnlyList<ValidationError> Errors => _errors;

    public bool IsValid => _errors.Count == 0;

    public void Add(string field, string code, string message) =>
        _errors.Add(new ValidationError(field, code, message));

    public void AddRange(IEnumerable<ValidationError> errors) => _errors.AddRange(errors);

    /// <summary>
    /// The top-level ProblemDetails <c>code</c> for this result. Spec §6 gives
    /// suspicious coordinates their own code; everything else is generic.
    /// </summary>
    public string TopLevelCode =>
        _errors.Any(e => e.Code == FieldErrorCodes.Suspicious)
            ? ErrorCodes.SuspiciousCoordinates
            : ErrorCodes.ValidationFailed;
}
