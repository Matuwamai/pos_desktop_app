namespace Pos.Core.Pricing;

public static class Money
{
    public static decimal Round(decimal value, int decimalDigits) => Math.Round(value, decimalDigits, MidpointRounding.AwayFromZero);

    // Generalizes Round from "round to a decimal-place count" to "round to an arbitrary
    // unit" (e.g. nearest 0.05) — cash rounding (Pos.Core.Pricing.CashRounding). A
    // falsy unit is a no-op. decimal arithmetic is exact here (unlike the server's JS
    // doubles), so no floating-point cleanup pass is needed afterward.
    public static decimal RoundToNearest(decimal amount, decimal unit) =>
        unit == 0m ? amount : Math.Round(amount / unit, 0, MidpointRounding.AwayFromZero) * unit;
}
