namespace Pos.Core.Pricing;

public sealed record PricedLine(decimal UnitPrice, int Quantity, decimal LineSubtotal, decimal TaxAmount, decimal LineTotal);

/// <summary>Order-level totals, mirroring the aggregation in <c>createOrder</c> (same file).</summary>
// CashRoundingAdjustment defaults to 0 here — pricing math itself never applies cash
// rounding (Pos.Core.Pricing.CashRounding is a payment-time concern, computed in
// Cart.ToOfflineOrderPayload, not here); SaleService threads the real value through.
public sealed record OrderTotals(decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal Total, decimal CashRoundingAdjustment = 0m);
public sealed record DiscountInput(decimal Amount);
public static class PricingMath
{
    public static PricedLine PriceLine(decimal unitPrice, decimal modifiersTotal, int quantity, decimal? taxRate, int decimalDigits)
    {
        var lineSubtotal = Money.Round((unitPrice + modifiersTotal) * quantity, decimalDigits);
        var taxAmount = taxRate.HasValue ? Money.Round(lineSubtotal * taxRate.Value, decimalDigits) : 0m;
        var lineTotal = Money.Round(lineSubtotal + taxAmount, decimalDigits);
        return new PricedLine(unitPrice, quantity, lineSubtotal, taxAmount, lineTotal);
    }

    public static OrderTotals ComputeTotals(IReadOnlyList<PricedLine> lines, IReadOnlyList<DiscountInput> discounts, int decimalDigits)
    {
        var subtotal = Money.Round(lines.Sum(l => l.LineSubtotal), decimalDigits);
        var taxTotal = Money.Round(lines.Sum(l => l.TaxAmount), decimalDigits);
        var discountTotal = Money.Round(discounts.Sum(d => d.Amount), decimalDigits);
        var total = Money.Round(subtotal - discountTotal + taxTotal, decimalDigits);
        return new OrderTotals(subtotal, discountTotal, taxTotal, total);
    }
}
