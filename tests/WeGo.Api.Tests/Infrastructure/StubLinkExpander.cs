using WeGo.Infrastructure.Geocoding;

namespace WeGo.Api.Tests.Infrastructure;

/// <summary>
/// Stands in for the short-link expander so the suite never makes an outbound
/// request — least of all to a URL a test supplied.
/// </summary>
public sealed class StubLinkExpander : ILinkExpander
{
    private readonly List<Uri> _calls = [];

    /// <summary>What every short link expands to. Null means "could not follow".</summary>
    public string? ExpandsTo { get; set; } =
        "https://www.google.com/maps/place/Th%C3%A1c+D%E1%BA%A3i+Y%E1%BA%BFm/"
        + "@20.817975,104.591686,17z/data=!4m6!3m5!8m2!3d20.817975!4d104.591686";

    public IReadOnlyList<Uri> Calls => _calls;

    public Task<string?> ExpandAsync(Uri shortUrl, CancellationToken cancellationToken)
    {
        _calls.Add(shortUrl);
        return Task.FromResult(ExpandsTo);
    }
}
