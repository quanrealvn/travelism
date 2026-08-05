using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace WeGo.Api.Auth;

/// <summary>One membership the browser holds: which member, on which trip.</summary>
public readonly record struct TripMembership(Guid TripId, Guid MemberId);

/// <summary>
/// Every trip this browser belongs to, most recently used first.
/// </summary>
/// <remarks>
/// A browser plans more than one trip — last summer's and next month's — so the
/// session is a set of memberships rather than a single one. It is still only a
/// claim of identity: authority comes from re-reading the member row on every
/// trip-scoped request, so a membership listed here that has since been removed
/// grants nothing.
/// </remarks>
public sealed record SessionToken(IReadOnlyList<TripMembership> Memberships)
{
    public SessionToken(Guid tripId, Guid memberId)
        : this([new TripMembership(tripId, memberId)])
    {
    }

    /// <summary>The member this browser is on the given trip, if it is on it at all.</summary>
    public bool TryFind(Guid tripId, out Guid memberId)
    {
        foreach (var membership in Memberships)
        {
            if (membership.TripId == tripId)
            {
                memberId = membership.MemberId;
                return true;
            }
        }

        memberId = default;
        return false;
    }

    /// <summary>True when another trip would not fit — see <see cref="With"/>.</summary>
    public bool IsFull => Memberships.Count >= SessionTokenService.MaxMemberships;

    /// <summary>
    /// The same session with <paramref name="membership"/> at the front.
    /// <para>
    /// Re-joining a trip already held replaces the old entry rather than adding
    /// a second: a browser has exactly one identity per trip, and two entries
    /// would make which one wins depend on ordering.
    /// </para>
    /// <para>
    /// Never evicts. An earlier version dropped the least recently used trip to
    /// make room, which silently cost people trips they still wanted — the trip
    /// stayed on the server but its invite code is only ever shown inside it, so
    /// for a trip you owned it was gone. Callers check <see cref="IsFull"/> and
    /// refuse instead, which is a thing the person can act on.
    /// </para>
    /// </summary>
    public SessionToken With(TripMembership membership)
    {
        var next = new List<TripMembership>(Memberships.Count + 1) { membership };
        foreach (var existing in Memberships)
        {
            if (existing.TripId != membership.TripId)
            {
                next.Add(existing);
            }
        }

        return new SessionToken(next);
    }

    /// <summary>The same session without the given trip — "forget this on this device".</summary>
    public SessionToken Without(Guid tripId) =>
        new([.. Memberships.Where(m => m.TripId != tripId)]);
}

/// <summary>
/// Issues and verifies the signed cookie payload from spec §5.7.
/// Format: <c>base64url(tripId:memberId[,tripId:memberId…]) . base64url(HMACSHA256(payload))</c>.
/// A single-membership payload is the one-element case of the same format, so
/// cookies issued before the session became a list still verify and still work.
/// The token is a bearer of identity only — it never grants authority by itself.
/// Membership is re-checked against the database on every trip-scoped request,
/// so revoking a member takes effect immediately rather than at cookie expiry.
/// </summary>
public sealed class SessionTokenService
{
    /// <summary>
    /// Browsers drop cookies over roughly 4KB. Each membership costs 66 bytes of
    /// payload, which base64 inflates by a third, so this bound keeps the whole
    /// cookie near 2KB however many trips somebody accumulates. Past this, the
    /// least recently used trip falls off the device — the trip itself is
    /// untouched and rejoining with the invite code restores it.
    /// </summary>
    public const int MaxMemberships = 20;

    private readonly byte[] _key;

    public SessionTokenService(byte[] signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        if (signingKey.Length < 32)
        {
            throw new ArgumentException("Signing key must be at least 32 bytes.", nameof(signingKey));
        }

        _key = signingKey;
    }

    public string Issue(SessionToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var payload = Encoding.UTF8.GetBytes(
            string.Join(',', token.Memberships.Select(m => $"{m.TripId:N}:{m.MemberId:N}")));
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    /// <summary>
    /// Verifies signature and shape. Returns false for anything malformed rather
    /// than throwing, since the input is entirely attacker-controlled.
    /// </summary>
    public bool TryValidate(string? value, out SessionToken token)
    {
        token = new SessionToken([]);

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var separator = value.IndexOf('.');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        if (!TryFromBase64Url(value.AsSpan(0, separator), out var payload)
            || !TryFromBase64Url(value.AsSpan(separator + 1), out var signature))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_key, payload);

        // Fixed-time comparison: a length-or-content short circuit here would
        // leak the signature one byte at a time to a patient attacker.
        if (!CryptographicOperations.FixedTimeEquals(expected, signature))
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(payload);
        var entries = text.Split(',', StringSplitOptions.RemoveEmptyEntries);

        // A signed payload cannot exceed the cap unless this service issued an
        // over-long one, so this is a guard against our own future bugs rather
        // than against the caller.
        if (entries.Length is 0 or > MaxMemberships)
        {
            return false;
        }

        var memberships = new List<TripMembership>(entries.Length);
        var seen = new HashSet<Guid>(entries.Length);

        foreach (var entry in entries)
        {
            var colon = entry.IndexOf(':');
            if (colon <= 0
                || !Guid.TryParseExact(entry[..colon], "N", out var tripId)
                || !Guid.TryParseExact(entry[(colon + 1)..], "N", out var memberId)
                // A duplicated trip would make the resolved member depend on
                // scan order. Refuse the whole token rather than pick one.
                || !seen.Add(tripId))
            {
                return false;
            }

            memberships.Add(new TripMembership(tripId, memberId));
        }

        token = new SessionToken(memberships);
        return true;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryFromBase64Url(ReadOnlySpan<char> value, out byte[] bytes)
    {
        bytes = [];

        var normalized = new string(value).Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            2 => normalized + "==",
            3 => normalized + "=",
            0 => normalized,
            _ => string.Empty,
        };

        if (normalized.Length == 0)
        {
            return false;
        }

        var buffer = new byte[Base64.GetMaxDecodedFromUtf8Length(normalized.Length)];
        if (!Convert.TryFromBase64String(normalized, buffer, out var written))
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }
}
