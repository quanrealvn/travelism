using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace WeGo.Infrastructure.Routing;

public sealed class OsrmOptions
{
    public const string SectionName = "Osrm";

    public string BaseAddress { get; set; } = "https://router.project-osrm.org/";

    /// <summary>Spec §5.4: 3 second timeout.</summary>
    public int TimeoutSeconds { get; set; } = 3;

    /// <summary>Spec §5.4: one retry, so at most two attempts.</summary>
    public int Retries { get; set; } = 1;
}

/// <summary>
/// Driving times from the public OSRM server (spec §5.4).
/// <para>
/// Every failure mode collapses to null, because the caller's response to all
/// of them is identical: estimate from straight-line distance and mark the
/// result as an estimate. A timeout, a 500, and a valid 200 saying "no route
/// exists" (spec §7.5) are equally unhelpful for planning a day.
/// </para>
/// </summary>
public sealed class OsrmRouteProvider(HttpClient httpClient, OsrmOptions options, ILogger<OsrmRouteProvider> logger)
    : IRouteProvider
{
    public async Task<RouteResult?> GetDrivingRouteAsync(
        double fromLat,
        double fromLng,
        double toLat,
        double toLng,
        CancellationToken cancellationToken)
    {
        // OSRM takes lon,lat — the opposite order to almost everything else,
        // and silently returns a plausible route for the wrong continent if
        // they are swapped.
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"route/v1/driving/{fromLng:0.######},{fromLat:0.######};{toLng:0.######},{toLat:0.######}?overview=false");

        for (var attempt = 0; attempt <= Math.Max(0, options.Retries); attempt++)
        {
            try
            {
                using var response = await httpClient
                    .GetAsync(url, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogInformation("OSRM answered {Status}.", (int)response.StatusCode);
                    continue;
                }

                var payload = await response.Content
                    .ReadFromJsonAsync<OsrmResponse>(cancellationToken)
                    .ConfigureAwait(false);

                return Map(payload);
            }
            catch (HttpRequestException ex)
            {
                logger.LogInformation(ex, "OSRM request failed (attempt {Attempt}).", attempt + 1);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("OSRM timed out (attempt {Attempt}).", attempt + 1);
            }
            catch (System.Text.Json.JsonException ex)
            {
                logger.LogInformation(ex, "OSRM returned a body that could not be parsed.");
                return null;
            }
        }

        return null;
    }

    private static RouteResult? Map(OsrmResponse? payload)
    {
        // Spec §7.5: a 200 with no usable route is a fallback case, not an error.
        if (payload is null
            || !string.Equals(payload.Code, "Ok", StringComparison.OrdinalIgnoreCase)
            || payload.Routes is not { Count: > 0 })
        {
            return null;
        }

        var route = payload.Routes[0];
        if (route.Duration is not { } seconds || route.Distance is not { } metres)
        {
            return null;
        }

        // Rounded up: understating travel is what makes a plan quietly impossible.
        return new RouteResult((int)Math.Ceiling(seconds / 60.0), (int)Math.Round(metres));
    }

    private sealed record OsrmResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("routes")]
        public List<OsrmRoute>? Routes { get; init; }
    }

    private sealed record OsrmRoute
    {
        [JsonPropertyName("duration")]
        public double? Duration { get; init; }

        [JsonPropertyName("distance")]
        public double? Distance { get; init; }
    }
}
