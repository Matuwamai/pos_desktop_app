namespace Pos.Core.Pricing;

/// <summary>
/// How many decimal places a currency's minor unit actually has — the same hand-
/// maintained list as the server's <c>config/currencies.js</c> (no shared codegen
/// across the JS/C#/Dart codebases in this project, so this is kept in sync by hand).
/// Everything not listed defaults to 2, which covers the large majority of world
/// currencies (USD, EUR, GBP, KES, NGN, INR, …). 3-decimal currencies (KWD, BHD, OMR,
/// JOD, TND, LYD) are unsupported — see the server-side plan notes.
/// </summary>
public static class CurrencyInfo
{
    private static readonly HashSet<string> ZeroDecimalCodes = new(StringComparer.Ordinal)
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF",
    };

    public static int GetDecimalDigits(string? currencyCode) => ZeroDecimalCodes.Contains(currencyCode ?? "") ? 0 : 2;
}
