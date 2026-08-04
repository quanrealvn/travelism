using Microsoft.Extensions.Logging;
using WeGo.Domain.Places;

namespace WeGo.Infrastructure.Geocoding;

/// <summary>
/// Resolves <c>maps.app.goo.gl</c> style links by asking where they redirect.
/// <para>
/// This makes an outbound request to a URL the user supplied, so it is guarded
/// twice: <see cref="PlaceLink.TryGetExpandableUrl"/> admits only the two
/// Google shorteners before we get here, and every hop of the redirect chain is
/// re-checked below. Automatic redirect following is switched off precisely so
/// each hop can be inspected — otherwise a shortener that redirected to
/// <c>169.254.169.254</c> would be followed without anyone looking.
/// </para>
/// </summary>
public sealed class GoogleLinkExpander(HttpClient httpClient, ILogger<GoogleLinkExpander> logger)
    : ILinkExpander
{
    /// <summary>Shorteners normally resolve in one hop; a few add an interstitial.</summary>
    private const int MaxRedirects = 5;

    public async Task<string?> ExpandAsync(Uri shortUrl, CancellationToken cancellationToken)
    {
        var current = shortUrl;

        for (var hop = 0; hop < MaxRedirects; hop++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                response = await httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                logger.LogInformation(ex, "Could not follow shortened map link.");
                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Timed out following shortened map link.");
                return null;
            }

            using (response)
            {
                var location = response.Headers.Location;
                if (location is null)
                {
                    // Not a redirect: this is as far as the chain goes. The URL
                    // we asked for is the answer, if it holds coordinates.
                    return current.ToString();
                }

                var next = location.IsAbsoluteUri ? location : new Uri(current, location);

                if (next.Scheme != Uri.UriSchemeHttps && next.Scheme != Uri.UriSchemeHttp)
                {
                    logger.LogWarning("Shortened map link redirected to a non-HTTP scheme; refusing.");
                    return null;
                }

                // A google.com/maps/... target is the expected destination, and
                // there is nothing further to follow.
                if (!PlaceLink.TryGetExpandableUrl(next.ToString(), out _))
                {
                    return next.ToString();
                }

                current = next;
            }
        }

        logger.LogInformation("Shortened map link exceeded {MaxRedirects} redirects.", MaxRedirects);
        return null;
    }
}
