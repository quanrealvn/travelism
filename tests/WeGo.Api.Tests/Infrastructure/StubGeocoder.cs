using WeGo.Infrastructure.Geocoding;

namespace WeGo.Api.Tests.Infrastructure;

/// <summary>
/// Replaces the real geocoder so the suite never touches OpenStreetMap: tests
/// must be deterministic and offline, and hammering a free shared service from
/// CI would be antisocial regardless.
/// </summary>
public sealed class StubGeocoder : IGeocoder
{
    private readonly List<(string Query, int Limit, (double Lat, double Lng)? Near)> _calls = [];

    /// <summary>Results handed back for any query. Overwrite per test.</summary>
    public List<GeocodeSearchResult> Results { get; } =
    [
        new("Thác Dải Yếm", "Thác Dải Yếm, Mộc Châu, Sơn La, Việt Nam", 20.8333, 104.6667, "waterfall"),
        new("Đồi chè trái tim", "Đồi chè trái tim, Mộc Châu, Sơn La, Việt Nam", 20.8500, 104.6500, "attraction"),
    ];

    /// <summary>When set, every search fails with it — for the 502 path.</summary>
    public GeocodingUnavailableException? FailWith { get; set; }

    public IReadOnlyList<(string Query, int Limit, (double Lat, double Lng)? Near)> Calls => _calls;

    public Task<IReadOnlyList<GeocodeSearchResult>> SearchAsync(
        string query,
        int limit,
        (double Lat, double Lng)? near,
        CancellationToken cancellationToken)
    {
        _calls.Add((query, limit, near));

        if (FailWith is not null)
        {
            throw FailWith;
        }

        return Task.FromResult<IReadOnlyList<GeocodeSearchResult>>(Results);
    }
}
