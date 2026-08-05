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

    /// <summary>
    /// Every request from one address, static assets included.
    ///
    /// Generous on purpose. A cold page load is roughly twenty requests, and a
    /// café, a school or a mobile carrier behind CGNAT presents dozens of real
    /// people as one address — this has to be a flood detector, not a quota.
    /// </summary>
    public int GlobalPerMinute { get; set; } = 600;

    /// <summary>
    /// The one endpoint that grows the database without anybody being logged
    /// in, so it is the only way an anonymous caller can consume disk. A person
    /// plans a handful of trips a year; five an hour is already absurd.
    /// </summary>
    public int CreateTripPerHour { get; set; } = 5;

    /// <summary>
    /// Place search and link resolution both call Nominatim, whose usage policy
    /// is enforced by banning the caller. Abuse here does not cost money, it
    /// costs everyone the feature — so this is stricter than the load alone
    /// would justify.
    /// </summary>
    public int GeocodePerMinute { get; set; } = 30;
}

public static class RateLimitPolicies
{
    public const string Join = "join";
    public const string CreateTrip = "create-trip";
    public const string Geocode = "geocode";

    public static IServiceCollection AddWeGoRateLimiting(
        this IServiceCollection services,
        RateLimitOptions options)
    {
        services.AddRateLimiter(limiter =>
        {
            /*
             * The backstop, across every endpoint and every static file.
             *
             * The per-endpoint policies below stop targeted abuse of the
             * expensive routes; this one stops the cheap, untargeted flood that
             * would otherwise keep a scale-to-zero machine awake indefinitely
             * and bill for it.
             */
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.GlobalPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));

            limiter.AddPolicy(CreateTrip, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.CreateTripPerHour,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            limiter.AddPolicy(Geocode, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.GeocodePerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

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
                // The wording stays generic now that several policies share
                // this handler — it used to say "join attempts" whatever was
                // actually refused.
                await Results.Problem(
                        detail: "Quá nhiều yêu cầu. Vui lòng đợi một lát rồi thử lại.",
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
