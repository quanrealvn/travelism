using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace WeGo.Api.Auth;

/// <summary>The identity a request carries: which member, on which trip.</summary>
public readonly record struct SessionToken(Guid TripId, Guid MemberId);

/// <summary>
/// Issues and verifies the signed cookie payload from spec §5.7.
/// Format: <c>base64url(tripId:memberId) . base64url(HMACSHA256(payload))</c>.
/// The token is a bearer of identity only — it never grants authority by itself.
/// Membership is re-checked against the database on every trip-scoped request,
/// so revoking a member takes effect immediately rather than at cookie expiry.
/// </summary>
public sealed class SessionTokenService
{
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
        var payload = Encoding.UTF8.GetBytes($"{token.TripId:N}:{token.MemberId:N}");
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    /// <summary>
    /// Verifies signature and shape. Returns false for anything malformed rather
    /// than throwing, since the input is entirely attacker-controlled.
    /// </summary>
    public bool TryValidate(string? value, out SessionToken token)
    {
        token = default;

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
        var colon = text.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        if (!Guid.TryParseExact(text[..colon], "N", out var tripId)
            || !Guid.TryParseExact(text[(colon + 1)..], "N", out var memberId))
        {
            return false;
        }

        token = new SessionToken(tripId, memberId);
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
