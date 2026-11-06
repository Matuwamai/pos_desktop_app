namespace Pos.Core.Pricing;

// Cash-rounding units ("Swedish rounding") for currencies whose smallest coin has been
// withdrawn — mirrors the server's config/cashRounding.js exactly, including the same
// deliberate omission of EUR and every other currency not in this table (cash rounding
// in a multi-country currency zone is a per-country rule this app has no way to apply
// correctly, since it only models currency, not country).
public static class CashRounding
{
    private static readonly Dictionary<string, decimal> DefaultUnits = new(StringComparer.Ordinal)
    {
        ["AUD"] = 0.05m,
        ["NZD"] = 0.10m,
        ["CAD"] = 0.05m,
        ["CHF"] = 0.05m,
        ["SEK"] = 1m,
        ["NOK"] = 1m,
        ["DKK"] = 0.50m,
    };

    public static decimal GetDefaultUnit(string currencyCode) => DefaultUnits.GetValueOrDefault(currencyCode, 0m);

    // tenantOverride null means "use the currency default"; any explicit value
    // (including 0, "disabled") takes precedence.
    public static decimal GetEffectiveUnit(string currencyCode, decimal? tenantOverride) =>
        tenantOverride ?? GetDefaultUnit(currencyCode);
}
