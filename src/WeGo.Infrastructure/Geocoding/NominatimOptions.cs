namespace WeGo.Infrastructure.Geocoding;

public sealed class NominatimOptions
{
    public const string SectionName = "Geocoding";

    public string BaseAddress { get; set; } = "https://nominatim.openstreetmap.org/";

    /// <summary>
    /// Nominatim's usage policy requires a User-Agent identifying the
    /// application and a way to contact its operator; requests without one are
    /// refused. Change the contact address before deploying this anywhere real.
    /// </summary>
    public string UserAgent { get; set; } = "WeGo-TripPlanner/0.1 (+https://github.com/wego/trip-planner)";

    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// The public instance allows at most one request per second. Requests are
    /// serialised behind this gap rather than fired in parallel.
    /// </summary>
    public int MinIntervalMs { get; set; } = 1000;

    public int CacheMinutes { get; set; } = 30;

    /// <summary>Half-width of the result-biasing box, in degrees (~55 km).</summary>
    public double BiasBoxDegrees { get; set; } = 0.5;

    /// <summary>Preferred language for returned names.</summary>
    public string AcceptLanguage { get; set; } = "vi,en";
}
