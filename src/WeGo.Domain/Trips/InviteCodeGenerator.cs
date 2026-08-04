using System.Security.Cryptography;

namespace WeGo.Domain.Trips;

/// <summary>
/// Spec §3: 8 characters, cryptographically random, unique per trip.
/// Uniqueness itself is the caller's job (§7.11: retry on collision, give up
/// after 5); this type only guarantees unbiased randomness.
/// </summary>
public static class InviteCodeGenerator
{
    /// <summary>
    /// Crockford-style alphabet: no I, L, O, U, 0 or 1, so a code read aloud or
    /// copied off a phone screen cannot be ambiguous. 30 symbols ^ 8 ≈ 6.6e11.
    /// </summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    public const int Length = 8;

    public const int MaxGenerationAttempts = 5;

    /// <summary>
    /// Largest multiple of the alphabet size that fits in a byte. Values at or
    /// above it are resampled rather than folded with '%', which would otherwise
    /// make the first few symbols measurably more likely than the rest.
    /// </summary>
    private static readonly int RejectionLimit = 256 - (256 % Alphabet.Length);

    public static string Generate() => Generate(static buffer => RandomNumberGenerator.Fill(buffer));

    /// <summary>Seam for testing the rejection-sampling loop with a deterministic byte source.</summary>
    internal static string Generate(Action<byte[]> fillRandom)
    {
        var code = new char[Length];
        var buffer = new byte[1];

        for (var i = 0; i < Length; i++)
        {
            byte value;
            do
            {
                fillRandom(buffer);
                value = buffer[0];
            }
            while (value >= RejectionLimit);

            code[i] = Alphabet[value % Alphabet.Length];
        }

        return new string(code);
    }

    /// <summary>Codes are compared uppercase so a lowercase paste still joins the trip.</summary>
    public static string Normalize(string code) => code.Trim().ToUpperInvariant();
}
