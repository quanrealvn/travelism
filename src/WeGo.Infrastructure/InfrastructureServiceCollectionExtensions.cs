using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WeGo.Domain.Abstractions;
using WeGo.Infrastructure.Geocoding;
using WeGo.Infrastructure.Persistence;

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
