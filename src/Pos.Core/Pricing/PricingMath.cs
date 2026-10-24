namespace Pos.Core.Pricing;

/// <summary>One priced sale line, mirroring <c>priceAndValidateItem</c> in <c>server/services/order.js</c>.</summary>
public sealed record PricedLine(decimal UnitPrice, int Quantity, decimal LineSubtotal, decimal TaxAmount, decimal LineTotal);

/// <summary>Order-level totals, mirroring the aggregation in <c>createOrder</c> (same file).</summary>
public sealed record OrderTotals(decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal Total);

/// <summary>
/// A manual discretionary discount (<c>amount</c> given directly) or a promotion-code discount
/// (percentage/fixed, resolved against the subtotal). The desktop terminal only ever *replays*
/// an amount the server or the cashier already decided — it never resolves promotion codes
/// itself (Phase 2: promo codes with usage limits are online-only) — so this takes a plain amount.
/// </summary>
public sealed record DiscountInput(decimal Amount);

/// <summary>
/// C# port of the pricing arithmetic in <c>server/services/order.js</c>, so the desktop computes
/// (or re-validates, for an offline sale pushed later) the exact same totals as the server.
/// See <see cref="Money"/> for why decimal rounding is safe to use in place of the server's
/// float rounding. Keep this in lockstep with <c>order.js</c> if that file's math changes —
/// the golden fixtures in <c>tests/Pos.Tests/Fixtures/pricing-golden.json</c> will catch drift.
/// </summary>
public static class PricingMath
{
    /// <param name="taxRate">E.g. 0.16 for 16%. Null when no tax rate applies (no default and no variant-level rate).</param>
    public static PricedLine PriceLine(decimal unitPrice, decimal modifiersTotal, int quantity, decimal? taxRate)
    {
        var lineSubtotal = Money.Round2((unitPrice + modifiersTotal) * quantity);
        var taxAmount = taxRate.HasValue ? Money.Round2(lineSubtotal * taxRate.Value) : 0m;
        var lineTotal = Money.Round2(lineSubtotal + taxAmount);
        return new PricedLine(unitPrice, quantity, lineSubtotal, taxAmount, lineTotal);
    }

    public static OrderTotals ComputeTotals(IReadOnlyList<PricedLine> lines, IReadOnlyList<DiscountInput> discounts)
    {
        var subtotal = Money.Round2(lines.Sum(l => l.LineSubtotal));
        var taxTotal = Money.Round2(lines.Sum(l => l.TaxAmount));
        var discountTotal = Money.Round2(discounts.Sum(d => d.Amount));
        var total = Money.Round2(subtotal - discountTotal + taxTotal);
        return new OrderTotals(subtotal, discountTotal, taxTotal, total);
    }
}
