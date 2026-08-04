using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WeGo.Domain.Abstractions;
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

        return services;
    }
}
