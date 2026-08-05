using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace WeGo.Infrastructure.Weather;

public sealed class OpenMeteoOptions
{
    public const string SectionName = "Weather";

    public string BaseAddress { get; set; } = "https://api.open-meteo.com/";

    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Spec §5.5: cached per trip for three hours.</summary>
    public int CacheHours { get; set; } = 3;
}

/// <summary>Daily forecast from Open-Meteo (spec §5.5). Free, no key required.</summary>
public sealed class OpenMeteoWeatherProvider(
    HttpClient httpClient,
    ILogger<OpenMeteoWeatherProvider> logger) : IWeatherProvider
{
    public async Task<WeatherForecast?> GetDailyAsync(
        double lat,
        double lng,
        DateOnly from,
        DateOnly to,
        string timeZoneId,
        CancellationToken cancellationToken)
    {
        // The timezone is passed to Open-Meteo so its day boundaries are the
        // trip's, not UTC's — a forecast for "1 March" must mean the traveller's
        // 1 March (spec §7.10).
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"v1/forecast?latitude={lat:0.####}&longitude={lng:0.####}"
            + $"&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum"
            + $"&start_date={from:yyyy-MM-dd}&end_date={to:yyyy-MM-dd}"
            + $"&timezone={Uri.EscapeDataString(timeZoneId)}");

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Open-Meteo answered {Status}.", (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content
                .ReadFromJsonAsync<OpenMeteoResponse>(cancellationToken)
                .ConfigureAwait(false);

            return Map(payload, lat, lng, timeZoneId);
        }
        catch (HttpRequestException ex)
        {
            logger.LogInformation(ex, "Open-Meteo request failed.");
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Open-Meteo timed out.");
            return null;
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogInformation(ex, "Open-Meteo returned a body that could not be parsed.");
            return null;
        }
    }

    private static WeatherForecast? Map(
        OpenMeteoResponse? payload,
        double lat,
        double lng,
        string timeZoneId)
    {
        var daily = payload?.Daily;
        if (daily?.Time is not { Count: > 0 })
        {
            return null;
        }

        var days = new List<DailyForecast>(daily.Time.Count);
        for (var i = 0; i < daily.Time.Count; i++)
        {
            if (!DateOnly.TryParse(daily.Time[i], CultureInfo.InvariantCulture, out var date))
            {
                continue;
            }

            days.Add(new DailyForecast(
                date,
                At(daily.MaxTemperature, i),
                At(daily.MinTemperature, i),
                At(daily.Precipitation, i),
                (int?)At(daily.WeatherCode, i)));
        }

        return days.Count == 0 ? null : new WeatherForecast(lat, lng, timeZoneId, days);
    }

    /// <summary>
    /// Open-Meteo returns parallel arrays that can be shorter than the date
    /// list, so an index is read defensively rather than assumed present.
    /// </summary>
    private static double? At(IReadOnlyList<double?>? values, int index) =>
        values is not null && index < values.Count ? values[index] : null;

    private sealed record OpenMeteoResponse
    {
        [JsonPropertyName("daily")]
        public OpenMeteoDaily? Daily { get; init; }
    }

    private sealed record OpenMeteoDaily
    {
        [JsonPropertyName("time")]
        public List<string>? Time { get; init; }

        [JsonPropertyName("weather_code")]
        public List<double?>? WeatherCode { get; init; }

        [JsonPropertyName("temperature_2m_max")]
        public List<double?>? MaxTemperature { get; init; }

        [JsonPropertyName("temperature_2m_min")]
        public List<double?>? MinTemperature { get; init; }

        [JsonPropertyName("precipitation_sum")]
        public List<double?>? Precipitation { get; init; }
    }
}
