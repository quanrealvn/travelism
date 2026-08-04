using WeGo.Api.Errors;

namespace WeGo.Api.Common;

/// <summary>
/// Success-or-<see cref="Failure"/> from a service call. Rejections are values,
/// not exceptions: a 409 for a duplicate name is an expected outcome, and using
/// exceptions for it would make control flow hard to see and easy to swallow.
/// </summary>
public readonly struct Result<T>
{
    private Result(T value)
    {
        Value = value;
        Failure = null;
    }

    private Result(Failure failure)
    {
        Value = default;
        Failure = failure;
    }

    public T? Value { get; }

    public Failure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static Result<T> Ok(T value) => new(value);

    public static implicit operator Result<T>(Failure failure) => new(failure);
}

public static class ResultExtensions
{
    /// <summary>Maps a service result onto an HTTP response, rendering failures as ProblemDetails.</summary>
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value!) : Problems.From(result.Failure!);

    public static IResult ToOk<T>(this Result<T> result) => result.ToHttp(Results.Ok);
}
