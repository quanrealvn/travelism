using System.Security.Cryptography;
using System.Text;

namespace WeGo.Api.Auth;

/// <summary>
/// Who is allowed to start a trip on this deployment.
///
/// Creating a trip is the only write an unauthenticated stranger can perform,
/// and the only one that consumes disk — everything else needs either a session
/// cookie or an invite code. On a public host that is the single open door, so
/// this closes it with a shared code without touching how trips are shared:
/// joining still needs nothing but the invite link.
///
/// Empty means open, which is the right default for local development and for
/// anyone running this for themselves.
/// </summary>
public sealed class AccessOptions
{
    public const string SectionName = "Access";

    public string CreateTripCode { get; set; } = string.Empty;

    public bool IsRestricted => !string.IsNullOrWhiteSpace(CreateTripCode);

    /// <summary>
    /// Fixed-time comparison. A shared code is short and guessable enough that
    /// leaking its length or first differing character through response timing
    /// is worth avoiding, and the check costs nothing.
    /// </summary>
    public bool Accepts(string? candidate)
    {
        if (!IsRestricted)
        {
            return true;
        }

        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(CreateTripCode));
    }
}
