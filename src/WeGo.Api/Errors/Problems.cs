using Microsoft.AspNetCore.Mvc;

namespace WeGo.Api.Errors;

/// <summary>
/// Renders <see cref="Failure"/> as RFC 7807 ProblemDetails. Every non-2xx
/// response in the app goes through here so the shape is identical everywhere
/// and no stack trace can reach a client (spec §6).
/// </summary>
public static class Problems
{
    public const string CodeExtension = "code";
    public const string ErrorsExtension = "errors";

    public static IResult From(Failure failure)
    {
        var problem = new ProblemDetails
        {
            Status = failure.Status,
            Title = TitleFor(failure.Status),
            Detail = failure.Detail,
            Type = $"https://httpstatuses.io/{failure.Status}",
        };

        problem.Extensions[CodeExtension] = failure.Code;

        if (failure.Errors is { Count: > 0 })
        {
            problem.Extensions[ErrorsExtension] = failure.Errors
                .Select(e => new { field = e.Field, code = e.Code, message = e.Message })
                .ToArray();
        }

        if (failure.Extensions is not null)
        {
            foreach (var (key, value) in failure.Extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        return Results.Problem(problem);
    }

    public static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status405MethodNotAllowed => "Method Not Allowed",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        StatusCodes.Status502BadGateway => "Bad Gateway",
        _ => "Error",
    };
}
