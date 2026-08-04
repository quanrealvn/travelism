namespace WeGo.Infrastructure.Geocoding;

/// <summary>
/// Follows a shortened map link to the full URL it points at, so the
/// coordinates buried in it can be read.
/// </summary>
public interface ILinkExpander
{
    /// <summary>
    /// Returns the expanded URL, or null when the link could not be followed.
    /// Never throws for an unreachable or hostile target: a bad paste is
    /// ordinary user input, not an exceptional condition.
    /// </summary>
    Task<string?> ExpandAsync(Uri shortUrl, CancellationToken cancellationToken);
}
