using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WeGo.Domain.Abstractions;
using WeGo.Infrastructure.Geocoding;
using WeGo.Infrastructure.Persistence;
using WeGo.Infrastructure.Routing;
using WeGo.Infrastructure.Weather;

namespace WeGo.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddWeGoInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new DatabaseOptions();
        configuration.GetSection(DatabaseOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<SqlitePragmaInterceptor>();

        services.AddDbContext<WeGoDbContext>((provider, builder) =>
        {
            builder.UseSqlite(DatabaseInitializer.BuildConnectionString(options));
            builder.AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>());
        });

        services.AddWeGoGeocoding(configuration);
        services.AddWeGoRouting(configuration);
        services.AddWeGoWeather(configuration);

        return services;
    }

    private static IServiceCollection AddWeGoWeather(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new OpenMeteoOptions();
        configuration.GetSection(OpenMeteoOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>(client =>
        {
            client.BaseAddress = new Uri(options.BaseAddress);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }

    private static IServiceCollection AddWeGoRouting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new OsrmOptions();
        configuration.GetSection(OsrmOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddHttpClient<IRouteProvider, OsrmRouteProvider>(client =>
        {
            client.BaseAddress = new Uri(options.BaseAddress);
            // Spec §5.4: 3 seconds. A routing service that is slow is, for
            // planning purposes, a routing service that is down.
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }

    private static IServiceCollection AddWeGoGeocoding(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new NominatimOptions();
        configuration.GetSection(NominatimOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddMemoryCache();

        services.AddHttpClient<ILinkExpander, GoogleLinkExpander>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // Off deliberately: each hop is re-checked against the allowlist in
            // GoogleLinkExpander, which cannot happen if the handler chases
            // redirects on its own.
            AllowAutoRedirect = false,
        });

        services.AddHttpClient<IGeocoder, NominatimGeocoder>(client =>
        {
            client.BaseAddress = new Uri(options.BaseAddress);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

            // Nominatim refuses requests that do not identify their caller.
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
