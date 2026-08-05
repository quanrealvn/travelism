using System.Security.Cryptography;
using Microsoft.AspNetCore.HttpOverrides;
using WeGo.Api;
using WeGo.Api.Auth;
using WeGo.Api.Endpoints;
using WeGo.Api.Errors;
using WeGo.Api.Services;
using WeGo.Domain.Common;
using WeGo.Infrastructure;
using WeGo.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var authOptions = new AuthOptions();
builder.Configuration.GetSection(AuthOptions.SectionName).Bind(authOptions);
builder.Services.AddSingleton(authOptions);

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

builder.Services.AddProblemDetails();

// By default minimal APIs answer a body that will not bind with a bare 400 and
// no body — outside Development, where they throw. Always throwing routes the
// failure through the exception handler instead, so a malformed body gets the
// same ProblemDetails treatment as every other rejection (spec §6).
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

var app = builder.Build();

// Populates RemoteIpAddress from X-Forwarded-For so the join rate limit
// partitions by the real client rather than by the reverse proxy.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

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

app.MapTripEndpoints();
app.MapPlaceEndpoints();
app.MapItineraryEndpoints();

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
