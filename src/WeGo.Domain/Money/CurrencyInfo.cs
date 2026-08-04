namespace WeGo.Domain.Money;

/// <summary>
/// Spec §5.3: all money is <c>long</c> minor units, formatted only at the edge.
/// The exponent says how many minor units make one major unit — VND is 0, so a
/// stored 100_001 really is ₫100,001 and integer division is exact accounting.
/// </summary>
public static class CurrencyInfo
{
    private static readonly Dictionary<string, int> ZeroDecimalOverrides = new(StringComparer.Ordinal)
    {
        ["VND"] = 0,
        ["JPY"] = 0,
        ["KRW"] = 0,
        ["CLP"] = 0,
        ["ISK"] = 0,
        ["XOF"] = 0,
        ["XAF"] = 0,
        ["XPF"] = 0,
        ["PYG"] = 0,
        ["RWF"] = 0,
        ["UGX"] = 0,
        ["VUV"] = 0,
        ["KMF"] = 0,
        ["DJF"] = 0,
        ["GNF"] = 0,
        ["BIF"] = 0,
    };

    public const int DefaultExponent = 2;

    /// <summary>ISO 4217 alpha-3: exactly three ASCII letters.</summary>
    public static bool IsWellFormed(string? currency)
    {
        if (currency is null || currency.Length != 3)
        {
            return false;
        }

        foreach (var c in currency)
        {
            if (!char.IsAsciiLetter(c))
            {
                return false;
            }
        }

        return true;
    }

    public static string Normalize(string currency) => currency.ToUpperInvariant();

    public static int GetExponent(string currency) =>
        ZeroDecimalOverrides.TryGetValue(Normalize(currency), out var exponent)
            ? exponent
            : DefaultExponent;
}
