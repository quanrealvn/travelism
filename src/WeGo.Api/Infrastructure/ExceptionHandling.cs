using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using WeGo.Api.Errors;
using WeGo.Domain.Common;

namespace WeGo.Api;

/// <summary>
/// Last line of the §6 error contract: whatever escapes an endpoint still leaves
/// as ProblemDetails with a stable code, and never as a stack trace.
/// </summary>
public static class ExceptionHandling
{
    public static void UseWeGoExceptionHandler(this WebApplication app)
    {
        app.UseExceptionHandler(builder => builder.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerFeature>();
            var exception = feature?.Error;

            var failure = MapException(exception);

            context.Response.StatusCode = failure.Status;
            await Problems.From(failure).ExecuteAsync(context).ConfigureAwait(false);
        }));
    }

    private static Failure MapException(Exception? exception) => exception switch
    {
        // A malformed or wrongly-typed body surfaces as BadHttpRequestException
        // from model binding, before any endpoint code runs. Translated here so
        // even that path answers with a code the client can branch on.
        BadHttpRequestException => new Failure(
            StatusCodes.Status400BadRequest,
            ErrorCodes.MalformedJson,
            "The request body could not be parsed as JSON matching this endpoint's schema."),

        JsonException => new Failure(
            StatusCodes.Status400BadRequest,
            ErrorCodes.MalformedJson,
            "The request body could not be parsed as JSON matching this endpoint's schema."),

        _ => new Failure(
            StatusCodes.Status500InternalServerError,
            ErrorCodes.InternalError,
            "An unexpected error occurred."),
    };
}
