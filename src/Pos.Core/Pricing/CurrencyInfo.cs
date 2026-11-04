namespace Pos.Core.Pricing;

public static class CurrencyInfo
{
    private static readonly HashSet<string> ZeroDecimalCodes = new(StringComparer.Ordinal)
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF",
    };

    public static int GetDecimalDigits(string? currencyCode) => ZeroDecimalCodes.Contains(currencyCode ?? "") ? 0 : 2;
}
