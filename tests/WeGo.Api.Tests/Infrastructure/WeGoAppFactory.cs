using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WeGo.Infrastructure.Geocoding;
using WeGo.Domain.Abstractions;
using WeGo.Infrastructure.Persistence;
using WeGo.Infrastructure.Routing;
using WeGo.Infrastructure.Weather;

namespace WeGo.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the real application over a private SQLite file. A file rather than
/// <c>:memory:</c> on purpose: the concurrency and WAL behaviour the spec cares
/// about (§7.9) only exists for a real database.
/// </summary>
public class WeGoAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Fixed so tokens stay valid across factories inside one test.</summary>
    private const string TestSigningKey = "dGVzdC1zaWduaW5nLWtleS0zMi1ieXRlcy1sb25nLXh4eHg=";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"wego-test-{Guid.NewGuid():N}.db");

    /// <summary>
    /// Raised well above the spec's 10/minute for most tests: every test in the
    /// suite shares one rate-limit partition (TestServer has no remote IP), so a
    /// production-tight limit here would make unrelated tests fail each other.
    /// <see cref="JoinRateLimitTests"/> overrides it back down to exercise it.
    /// </summary>
    public virtual int JoinPerMinute => 10_000;

    /// <summary>
    /// The stub standing in for OpenStreetMap. Tests mutate it to choose what
    /// the geocoder returns, or to make it fail.
    /// </summary>
    public StubGeocoder Geocoder { get; } = new();

    /// <summary>Stands in for following a shortened map link.</summary>
    public StubLinkExpander LinkExpander { get; } = new();

    /// <summary>Stands in for OSRM.</summary>
    public StubRouteProvider Routes { get; } = new();

    /// <summary>Stands in for Open-Meteo.</summary>
    public StubWeatherProvider Weather { get; } = new();

    /// <summary>
    /// Overridable so a test can freeze time — the weather rules turn on what
    /// "today" is in the trip's timezone.
    /// </summary>
    public virtual DateTimeOffset? FixedNow => null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", $"Data Source={_databasePath}");
        builder.UseSetting("Database:BusyTimeoutMs", "5000");
        builder.UseSetting("Database:EnableWal", "true");
        builder.UseSetting("Auth:SigningKey", TestSigningKey);
        builder.UseSetting(
            "RateLimits:JoinPerMinute",
            JoinPerMinute.ToString(CultureInfo.InvariantCulture));

        builder.ConfigureTestServices(services =>
        {
            // Removes the typed HttpClient registration as well as the service,
            // so nothing in the suite can reach the real OpenStreetMap.
            services.RemoveAll<IGeocoder>();
            services.AddSingleton<IGeocoder>(Geocoder);

            services.RemoveAll<ILinkExpander>();
            services.AddSingleton<ILinkExpander>(LinkExpander);

            services.RemoveAll<IRouteProvider>();
            services.AddSingleton<IRouteProvider>(Routes);

            services.RemoveAll<IWeatherProvider>();
            services.AddSingleton<IWeatherProvider>(Weather);

            if (FixedNow is { } now)
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FrozenClock(now));
            }
        });
    }

    /// <summary>A clock that does not move, for rules that depend on "today".</summary>
    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }


    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    /// <summary>Direct database access for asserting on state the API does not expose.</summary>
    public async Task<T> WithDbAsync<T>(Func<WeGoDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WeGoDbContext>();
        return await action(db);
    }

    public async Task WithDbAsync(Func<WeGoDbContext, Task> action) =>
        await WithDbAsync<object?>(async db =>
        {
            await action(db);
            return null;
        });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // WAL leaves -shm/-wal siblings behind; clean all three up.
        foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
        {
            TryDelete(_databasePath + suffix);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A pooled connection may still hold the handle; a stale temp file
            // is not worth failing an otherwise green test run over.
        }
    }
}
