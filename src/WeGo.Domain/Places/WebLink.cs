namespace WeGo.Domain.Places;

/// <summary>
/// Validation for a link a member saved against a place.
/// <para>
/// These are rendered as clickable anchors, which makes the scheme a security
/// decision rather than a formatting one: <c>javascript:</c> and <c>data:</c>
/// URLs execute when clicked, so the allowlist here is what stands between a
/// saved link and script running in another member's session.
/// </para>
/// </summary>
public static class WebLink
{
    /// <summary>Practical browser limit; well beyond any real article URL.</summary>
    public const int MaxUrlLength = 2000;

    public const int MaxLabelLength = 120;

    /// <summary>Enough for the sources behind one place without unbounded payloads.</summary>
    public const int MaxPerPlace = 10;

    /// <summary>
    /// True when this is a link safe to render as an anchor: absolute, http or
    /// https, and with a host.
    /// </summary>
    public static bool IsSafe(string? url)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxUrlLength)
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        // An allowlist, not a blocklist: the set of dangerous schemes is open
        // ended (javascript, data, vbscript, file, and whatever a browser adds
        // next), while the set we actually want is exactly two.
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return !string.IsNullOrEmpty(parsed.Host);
    }

    /// <summary>
    /// A short label for a link that has none — the host, without "www.".
    /// Better than showing a 200-character URL in a list.
    /// </summary>
    public static string DisplayNameFor(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            return url;
        }

        var host = parsed.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }
}
