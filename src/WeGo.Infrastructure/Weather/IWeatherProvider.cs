namespace WeGo.Infrastructure.Weather;

/// <summary>One day of forecast.</summary>
public sealed record DailyForecast(
    DateOnly Date,
    double? MaxTempC,
    double? MinTempC,
    double? PrecipitationMm,
    int? WeatherCode);

public sealed record WeatherForecast(
    double Lat,
    double Lng,
    string TimeZoneId,
    IReadOnlyList<DailyForecast> Days);

public interface IWeatherProvider
{
    /// <summary>
    /// Daily forecast for a point, or null when the upstream service could not
    /// answer. Null rather than an exception: spec §5.5 answers an outage from
    /// cache when it can, and the caller decides.
    /// </summary>
    Task<WeatherForecast?> GetDailyAsync(
        double lat,
        double lng,
        DateOnly from,
        DateOnly to,
        string timeZoneId,
        CancellationToken cancellationToken);
}
