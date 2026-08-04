using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace WeGo.Infrastructure.Geocoding;

/// <summary>
/// Place-name lookup against OpenStreetMap's Nominatim, the geocoder that
/// matches the tiles the map already uses.
/// <para>
/// It is a free shared service with a strict usage policy, so this client is
/// deliberately conservative: identical queries are cached, requests are
/// serialised behind a minimum interval, and a failure never propagates as an
/// unhandled exception — the caller gets
/// <see cref="GeocodingUnavailableException"/> and answers 502.
/// </para>
/// </summary>
public sealed class NominatimGeocoder(
    HttpClient httpClient,
    IMemoryCache cache,
    NominatimOptions options,
    ILogger<NominatimGeocoder> logger) : IGeocoder
{
    /// <summary>
    /// Serialises outbound calls so the one-request-per-second policy holds even
    /// when several people are typing at once.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<GeocodeSearchResult>> SearchAsync(
        string query,
        int limit,
        (double Lat, double Lng)? near,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(query, limit, near);
        if (cache.TryGetValue<IReadOnlyList<GeocodeSearchResult>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var url = BuildUrl(query, limit, near);

        try
        {
            var payload = await SendThrottledAsync(url, cancellationToken).ConfigureAwait(false);
            var results = payload
                .Select(Map)
                .OfType<GeocodeSearchResult>()
                .ToList();

            cache.Set(cacheKey, (IReadOnlyList<GeocodeSearchResult>)results,
                TimeSpan.FromMinutes(options.CacheMinutes));

            return results;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The HttpClient timeout, not the caller giving up.
            throw new GeocodingUnavailableException("The place search service timed out.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Nominatim request failed.");
            throw new GeocodingUnavailableException("The place search service is unavailable.", ex);
        }
    }

    private async Task<List<NominatimPlace>> SendThrottledAsync(
        string url,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastRequestAt;
            var minimum = TimeSpan.FromMilliseconds(options.MinIntervalMs);
            if (elapsed < minimum)
            {
                await Task.Delay(minimum - elapsed, cancellationToken).ConfigureAwait(false);
            }

            using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            _lastRequestAt = DateTimeOffset.UtcNow;

            if (!response.IsSuccessStatusCode)
            {
                throw new GeocodingUnavailableException(
                    $"The place search service answered {(int)response.StatusCode}.");
            }

            return await response.Content
                .ReadFromJsonAsync<List<NominatimPlace>>(cancellationToken)
                .ConfigureAwait(false) ?? [];
        }
        finally
        {
            Gate.Release();
        }
    }

    private string BuildUrl(string query, int limit, (double Lat, double Lng)? near)
    {
        var parameters = new List<string>
        {
            "format=jsonv2",
            "addressdetails=1",
            $"limit={limit.ToString(CultureInfo.InvariantCulture)}",
            $"q={Uri.EscapeDataString(query)}",
            $"accept-language={Uri.EscapeDataString(options.AcceptLanguage)}",
        };

        if (near is { } point)
        {
            // bounded=0: the box only ranks nearby hits higher, it does not
            // exclude anything — a trip can still add a place far from the rest.
            var d = options.BiasBoxDegrees;
            var left = (point.Lng - d).ToString("0.######", CultureInfo.InvariantCulture);
            var right = (point.Lng + d).ToString("0.######", CultureInfo.InvariantCulture);
            var top = (point.Lat + d).ToString("0.######", CultureInfo.InvariantCulture);
            var bottom = (point.Lat - d).ToString("0.######", CultureInfo.InvariantCulture);

            parameters.Add($"viewbox={left},{top},{right},{bottom}");
            parameters.Add("bounded=0");
        }

        return "search?" + string.Join('&', parameters);
    }

    private static string BuildCacheKey(string query, int limit, (double Lat, double Lng)? near)
    {
        var bias = near is { } point
            ? FormattableString.Invariant($"{point.Lat:0.##},{point.Lng:0.##}")
            : "none";

        return FormattableString.Invariant($"geocode:{query.ToLowerInvariant()}:{limit}:{bias}");
    }

    private static GeocodeSearchResult? Map(NominatimPlace place)
    {
        // Coordinates arrive as strings and are the only fields we cannot do
        // without, so a row that fails to parse is dropped rather than guessed.
        if (!double.TryParse(place.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(place.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
        {
            return null;
        }

        var displayName = place.DisplayName ?? string.Empty;
        var name = string.IsNullOrWhiteSpace(place.Name)
            ? displayName.Split(',')[0].Trim()
            : place.Name.Trim();

        if (name.Length == 0)
        {
            return null;
        }

        return new GeocodeSearchResult(name, displayName, lat, lng, place.Type);
    }

    private sealed record NominatimPlace
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; init; }

        [JsonPropertyName("lat")]
        public string? Lat { get; init; }

        [JsonPropertyName("lon")]
        public string? Lon { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }
    }
}
