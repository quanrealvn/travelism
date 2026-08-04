using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using WeGo.Api.Errors;
using WeGo.Domain.Common;

namespace WeGo.Api;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Spec §5.7: 10 join attempts per IP per minute.</summary>
    public int JoinPerMinute { get; set; } = 10;
}

public static class RateLimitPolicies
{
    public const string Join = "join";

    public static IServiceCollection AddWeGoRateLimiting(
        this IServiceCollection services,
        RateLimitOptions options)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(Join, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.JoinPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    // Excess attempts are rejected outright rather than queued:
                    // the point is to slow invite-code guessing, and a queue
                    // would just let the guesses through a moment later.
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                // Rejections still have to satisfy the §6 error contract, so the
                // body is ProblemDetails rather than the framework's empty 429.
                await Results.Problem(
                        detail: "Too many join attempts. Wait a minute and try again.",
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: Problems.TitleFor(StatusCodes.Status429TooManyRequests),
                        type: $"https://httpstatuses.io/{StatusCodes.Status429TooManyRequests}",
                        extensions: new Dictionary<string, object?>
                        {
                            [Problems.CodeExtension] = ErrorCodes.RateLimited,
                        })
                    .ExecuteAsync(context.HttpContext)
                    .ConfigureAwait(false);
            };
        });

        return services;
    }

    /// <summary>
    /// Partition by client IP. Behind a proxy the real address arrives via
    /// forwarded headers, which <c>UseForwardedHeaders</c> has already folded
    /// into RemoteIpAddress by the time this runs. A missing address (in-memory
    /// TestServer, unix sockets) shares one bucket rather than escaping the limit.
    /// </summary>
    private static string PartitionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
