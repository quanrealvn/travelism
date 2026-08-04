using System.Globalization;
using System.Text.RegularExpressions;

namespace WeGo.Domain.Places;

/// <summary>A location recovered from a pasted link or coordinate pair.</summary>
public sealed record ParsedPlaceLink(double Lat, double Lng, string? Name);

/// <summary>
/// Reads a location out of something the user pasted.
/// <para>
/// This exists because OpenStreetMap does not have every place in Vietnam, and
/// planning already happens by dropping a Google Maps link into a group chat.
/// Letting that link be pasted straight in means anything findable on Google
/// Maps is addable here — without this app being a Google customer, because the
/// searching was done on their side.
/// </para>
/// <para>Pure: URL text in, coordinates out. Expanding a short link is the caller's job.</para>
/// </summary>
public static partial class PlaceLink
{
    /// <summary>
    /// Hosts whose links may be fetched to discover where they redirect.
    /// <para>
    /// This is an allowlist rather than a blocklist on purpose. The URL comes
    /// from the user, and fetching it server-side is a request originating
    /// inside the network — an open version would let a caller probe
    /// <c>localhost</c>, cloud metadata endpoints, or anything else reachable
    /// from the host. Only the two Google shorteners are ever followed.
    /// </para>
    /// </summary>
    private static readonly string[] ExpandableHosts =
    [
        "maps.app.goo.gl",
        "goo.gl",
    ];

    /// <summary>True when the input looks like a URL rather than a place name.</summary>
    public static bool LooksLikeUrl(string input)
    {
        var trimmed = input.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when this is a shortened link that has to be followed before its
    /// coordinates can be read. Only allowlisted hosts qualify.
    /// </summary>
    public static bool TryGetExpandableUrl(string input, out Uri? url)
    {
        url = null;

        if (!LooksLikeUrl(input) || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        if (!ExpandableHosts.Contains(parsed.Host, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        url = parsed;
        return true;
    }

    /// <summary>
    /// Pulls a location out of a pasted link or a bare coordinate pair.
    /// Returns null when nothing usable is present.
    /// </summary>
    public static ParsedPlaceLink? Parse(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return ParseBareCoordinates(text) ?? ParseUrl(text);
    }

    /// <summary>"20.8386, 104.6383" — what you get from Google Maps' "copy coordinates".</summary>
    private static ParsedPlaceLink? ParseBareCoordinates(string text)
    {
        var match = BareCoordinatesPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        return Build(match.Groups[1].Value, match.Groups[2].Value, name: null);
    }

    private static ParsedPlaceLink? ParseUrl(string text)
    {
        if (!LooksLikeUrl(text))
        {
            return null;
        }

        var name = ExtractPlaceName(text);

        // Order matters. In a /maps/place/ URL the "!3d…!4d…" pair is the place
        // itself, while "@lat,lng" is only where the viewport happened to be —
        // they are often tens of metres apart, and occasionally much more.
        var placeData = PlaceDataPattern().Match(text);
        if (placeData.Success)
        {
            var parsed = Build(placeData.Groups[1].Value, placeData.Groups[2].Value, name);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        // ?q=lat,lng / ?ll=lat,lng / ?query=lat,lng, and OpenStreetMap's
        // ?mlat=&mlon= marker form.
        var queryCoordinates = QueryCoordinatesPattern().Match(text);
        if (queryCoordinates.Success)
        {
            var parsed = Build(queryCoordinates.Groups[1].Value, queryCoordinates.Groups[2].Value, name);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        var separateMarker = SeparateMarkerPattern().Match(text);
        if (separateMarker.Success)
        {
            var parsed = Build(separateMarker.Groups[1].Value, separateMarker.Groups[2].Value, name);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        // OpenStreetMap's "#map=zoom/lat/lng".
        var osmHash = OpenStreetMapHashPattern().Match(text);
        if (osmHash.Success)
        {
            var parsed = Build(osmHash.Groups[1].Value, osmHash.Groups[2].Value, name);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        // Viewport centre. Last resort: better than refusing the paste, and for
        // a link shared from a phone it is usually within metres of the pin.
        var viewport = ViewportPattern().Match(text);
        return viewport.Success
            ? Build(viewport.Groups[1].Value, viewport.Groups[2].Value, name)
            : null;
    }

    /// <summary>Recovers the display name from ".../maps/place/&lt;Name&gt;/...".</summary>
    private static string? ExtractPlaceName(string url)
    {
        var match = PlaceNamePattern().Match(url);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups[1].Value;
        if (raw.Length == 0)
        {
            return null;
        }

        try
        {
            // Google encodes spaces in the path segment as '+'.
            var decoded = Uri.UnescapeDataString(raw.Replace('+', ' ')).Trim();

            // A pin with no name renders as its coordinates; that is not a name.
            return decoded.Length == 0 || BareCoordinatesPattern().IsMatch(decoded) ? null : decoded;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static ParsedPlaceLink? Build(string latText, string lngText, string? name)
    {
        if (!double.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(lngText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
        {
            return null;
        }

        if (double.IsNaN(lat) || double.IsNaN(lng) || lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            return null;
        }

        // Same reasoning as PlaceRules: (0,0) is the Gulf of Guinea and is far
        // more likely to be a link that carried no location at all.
        if (lat == 0 && lng == 0)
        {
            return null;
        }

        return new ParsedPlaceLink(lat, lng, name);
    }

    [GeneratedRegex(@"^\s*(-?\d{1,3}(?:\.\d+)?)\s*[,\s]\s*(-?\d{1,3}(?:\.\d+)?)\s*$")]
    private static partial Regex BareCoordinatesPattern();

    [GeneratedRegex(@"!3d(-?\d{1,3}(?:\.\d+)?)!4d(-?\d{1,3}(?:\.\d+)?)")]
    private static partial Regex PlaceDataPattern();

    // The separator may arrive literal or percent-encoded depending on how the
    // link was shared; one alternation keeps the capture groups at 1 and 2.
    [GeneratedRegex(@"[?&](?:q|ll|query|daddr|center)=(-?\d{1,3}(?:\.\d+)?)(?:,|%2C)\s*(-?\d{1,3}(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex QueryCoordinatesPattern();

    [GeneratedRegex(@"[?&]mlat=(-?\d{1,3}(?:\.\d+)?)&mlon=(-?\d{1,3}(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex SeparateMarkerPattern();

    [GeneratedRegex(@"#map=\d+(?:\.\d+)?/(-?\d{1,3}(?:\.\d+)?)/(-?\d{1,3}(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex OpenStreetMapHashPattern();

    [GeneratedRegex(@"@(-?\d{1,3}(?:\.\d+)?),(-?\d{1,3}(?:\.\d+)?)")]
    private static partial Regex ViewportPattern();

    [GeneratedRegex(@"/maps/place/([^/@?]+)")]
    private static partial Regex PlaceNamePattern();
}
