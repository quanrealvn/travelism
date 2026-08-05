using System.Security.Cryptography;
using Microsoft.AspNetCore.HttpOverrides;
using WeGo.Api;
using WeGo.Api.Auth;
using WeGo.Api.Endpoints;
using WeGo.Api.Errors;
using WeGo.Api.Realtime;
using WeGo.Api.Services;
using WeGo.Domain.Common;
using WeGo.Infrastructure;
using WeGo.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var authOptions = new AuthOptions();
builder.Configuration.GetSection(AuthOptions.SectionName).Bind(authOptions);
builder.Services.AddSingleton(authOptions);

var accessOptions = new AccessOptions();
builder.Configuration.GetSection(AccessOptions.SectionName).Bind(accessOptions);
builder.Services.AddSingleton(accessOptions);

var rateLimitOptions = new RateLimitOptions();
builder.Configuration.GetSection(RateLimitOptions.SectionName).Bind(rateLimitOptions);
builder.Services.AddSingleton(rateLimitOptions);

builder.Services.AddSingleton(_ => new SessionTokenService(
    SigningKeyProvider.Resolve(authOptions, builder.Environment.ContentRootPath)));

builder.Services.AddWeGoInfrastructure(builder.Configuration);
builder.Services.AddWeGoRateLimiting(rateLimitOptions);

builder.Services.AddScoped<ActivityLogWriter>();
builder.Services.AddScoped<TripService>();
builder.Services.AddScoped<PlaceService>();
builder.Services.AddScoped<GeocodingService>();
builder.Services.AddScoped<ItineraryService>();
builder.Services.AddScoped<TravelTimeService>();
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddScoped<SnapshotService>();
builder.Services.AddScoped<WeatherService>();

builder.Services.AddSignalR();
builder.Services.AddSingleton<ITripBroadcaster, TripBroadcaster>();

builder.Services.AddProblemDetails();

// By default minimal APIs answer a body that will not bind with a bare 400 and
// no body — outside Development, where they throw. Always throwing routes the
// failure through the exception handler instead, so a malformed body gets the
// same ProblemDetails treatment as every other rejection (spec §6).
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

var app = builder.Build();

/*
 * Populates RemoteIpAddress and Scheme from the platform's proxy headers.
 *
 * KnownNetworks and KnownProxies must be cleared. They default to loopback
 * only, and a hosted reverse proxy never is — so out of the box the headers are
 * silently ignored, and both things that depend on them break quietly:
 *
 *   - Request.IsHttps stays false behind TLS termination, so the session cookie
 *     is issued without Secure.
 *   - RemoteIpAddress is the proxy's, so every visitor on earth shares one
 *     rate-limit partition and the per-IP limits protect nothing.
 *
 * Clearing them means trusting whatever sends these headers, which is correct
 * here because the container port is only reachable through the platform's
 * proxy. Spoofing is still handled: the default ForwardLimit of 1 takes the
 * rightmost X-Forwarded-For entry, which is the one the proxy itself appended,
 * so a client that invents its own is overridden rather than believed.
 */
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

app.UseWeGoExceptionHandler();

// Gives routing-level rejections (404 on an unknown path, 405 on a wrong verb)
// the same ProblemDetails shape as everything else.
app.UseStatusCodePages(async statusCodeContext =>
{
    var response = statusCodeContext.HttpContext.Response;
    var code = response.StatusCode switch
    {
        // The only bare 400 this API surface produces is a body that would not
        // bind; endpoint-level rejections are 422 with per-field detail.
        StatusCodes.Status400BadRequest => ErrorCodes.MalformedJson,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status405MethodNotAllowed => ErrorCodes.MethodNotAllowed,
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthenticated,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
        _ => ErrorCodes.InternalError,
    };

    await Problems.From(new Failure(
            response.StatusCode,
            code,
            Problems.TitleFor(response.StatusCode)))
        .ExecuteAsync(statusCodeContext.HttpContext);
});

app.UseRateLimiter();

app.UseDefaultFiles();
app.UseStaticFiles();

/*
 * Liveness, for the platform's health check.
 *
 * Deliberately does not touch the database: migrations run below before the
 * first request is served, so if they failed the process never reaches here at
 * all and the check fails by not answering. Querying on every probe would add
 * load to say something startup has already proven.
 */
// Exempt from rate limiting, including the global limiter. A throttled health
// check reads as an unhealthy app, and the platform's response to that is to
// restart the machine — turning a flood into an outage.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .DisableRateLimiting();

/*
 * What this deployment expects of a first-time visitor.
 *
 * Only whether a shared code is needed, never the code itself — the client has
 * to know whether to ask for one, and showing an access-code field on an open
 * instance would invent a barrier that is not there.
 */
app.MapGet("/config", (AccessOptions access) =>
    Results.Ok(new { requiresAccessCode = access.IsRestricted }));

app.MapTripEndpoints();
app.MapPlaceEndpoints();
app.MapItineraryEndpoints();
app.MapExpenseEndpoints();
app.MapWeatherEndpoints();

// Spec §5.8: one group per trip, joined with the same cookie the API uses.
app.MapHub<TripHub>("/hubs/trip");

// An unmatched path under the API surface must answer with the JSON error
// contract; only genuinely non-API paths fall through to the SPA shell.
app.MapFallback("/trips/{**rest}", () => Problems.From(Failure.NotFound("No such endpoint.")));
app.MapFallback("/session/{**rest}", () => Problems.From(Failure.NotFound("No such endpoint.")));
app.MapFallbackToFile("index.html");

await using (var scope = app.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetRequiredService<WeGoDbContext>();
    var databaseOptions = scope.ServiceProvider.GetRequiredService<DatabaseOptions>();
    await DatabaseInitializer.InitializeAsync(context, databaseOptions);
}

await app.RunAsync();

/// <summary>
/// Resolves the HMAC key for session cookies. A configured key always wins; with
/// none set, a random key is generated once and cached beside the database so a
/// restart does not sign every existing member out of their trip.
/// </summary>
internal static class SigningKeyProvider
{
    private const string KeyFileName = ".wego-signing-key";

    public static byte[] Resolve(AuthOptions options, string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(options.SigningKey))
        {
            return Convert.FromBase64String(options.SigningKey);
        }

        var path = Path.Combine(contentRootPath, KeyFileName);
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length > 0)
            {
                return Convert.FromBase64String(existing);
            }
        }

        var generated = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(path, Convert.ToBase64String(generated));
        return generated;
    }
}

/// <summary>Exposed so integration tests can host this app with WebApplicationFactory.</summary>
public partial class Program;
