using WeGo.Infrastructure.Weather;

namespace WeGo.Api.Tests.Infrastructure;

/// <summary>Stands in for Open-Meteo so the suite is deterministic and offline.</summary>
public sealed class StubWeatherProvider : IWeatherProvider
{
    private int _calls;

    /// <summary>Null models an upstream outage (spec §5.5).</summary>
    public bool Available { get; set; } = true;

    public int Calls => Volatile.Read(ref _calls);

    /// <summary>The timezone the last call asked for, to prove the trip's is used.</summary>
    public string? LastTimeZoneId { get; private set; }

    public DateOnly? LastFrom { get; private set; }

    public Task<WeatherForecast?> GetDailyAsync(
        double lat,
        double lng,
        DateOnly from,
        DateOnly to,
        string timeZoneId,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        LastTimeZoneId = timeZoneId;
        LastFrom = from;

        if (!Available)
        {
            return Task.FromResult<WeatherForecast?>(null);
        }

        var days = new List<DailyForecast>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            days.Add(new DailyForecast(date, 28.5, 18.0, 1.2, 61));
        }

        return Task.FromResult<WeatherForecast?>(
            new WeatherForecast(lat, lng, timeZoneId, days));
    }
}
