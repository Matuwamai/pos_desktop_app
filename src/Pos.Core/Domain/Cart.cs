using Pos.Core.Pricing;
using Pos.Core.Sync;

namespace Pos.Core.Domain;

public sealed record CartModifier(string Id, string Name, decimal PriceAdjustment);
public sealed class CartLine
{
    public required string VariantId { get; init; }
    public required string ProductName { get; init; }
    public required string Sku { get; init; }

    public required decimal BaseUnitPrice { get; init; }
    public required IReadOnlyList<CartModifier> Modifiers { get; init; }
    public required int Quantity { get; set; }
    public decimal? TaxRate { get; init; }

    public decimal ModifiersTotal => Modifiers.Sum(m => m.PriceAdjustment);

    public string Key => VariantId + "::" + string.Join(",", Modifiers.Select(m => m.Id).OrderBy(id => id, StringComparer.Ordinal));
}
public sealed class Cart
{
    public List<CartLine> Items { get; } = [];
    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public decimal? DiscountAmount { get; set; }
    public string? DiscountReason { get; set; }
    public int DecimalDigits { get; set; } = 2;
    public string? DiscountPromotionCode { get; set; }
    // Already resolved (tenant's own override, else the currency's default) — see
    // Pos.Core.Pricing.CashRounding. 0 means no rounding.
    public decimal CashRoundingUnit { get; set; }

    public void AddOrIncrement(CartLine line)
    {
        var existing = Items.FirstOrDefault(i => i.Key == line.Key);
        if (existing is not null) existing.Quantity += line.Quantity;
        else Items.Add(line);
    }

    public void RemoveLine(string key) => Items.RemoveAll(i => i.Key == key);

    public void SetQuantity(string key, int quantity)
    {
        if (quantity <= 0)
        {
            RemoveLine(key);
            return;
        }
        var line = Items.FirstOrDefault(i => i.Key == key);
        if (line is not null) line.Quantity = quantity;
    }

    public void ClearDiscount()
    {
        DiscountAmount = null;
        DiscountReason = null;
        DiscountPromotionCode = null;
    }

    public IReadOnlyList<PricedLine> PriceLines() =>
        [.. Items.Select(line => PricingMath.PriceLine(line.BaseUnitPrice, line.ModifiersTotal, line.Quantity, line.TaxRate, DecimalDigits))];

    public IReadOnlyList<DiscountInput> Discounts() =>
        DiscountAmount.HasValue && DiscountAmount.Value > 0 ? [new DiscountInput(DiscountAmount.Value)] : [];

    public OrderTotals ComputeTotals() => PricingMath.ComputeTotals(PriceLines(), Discounts(), DecimalDigits);
    public OfflineOrderPayload ToOfflineOrderPayload(
        string id, string locationId, string cashierId, string? cashDrawerSessionId,
        string terminalReceiptNo, DateTimeOffset createdAt, IReadOnlyList<OfflineOrderPayment> payments)
    {
        var totals = ComputeTotals();
        var items = new List<OfflineOrderItem>(Items.Count);
        foreach (var line in Items)
        {
            var priced = PricingMath.PriceLine(line.BaseUnitPrice, line.ModifiersTotal, line.Quantity, line.TaxRate, DecimalDigits);
            items.Add(new OfflineOrderItem
            {
                VariantId = line.VariantId,
                Quantity = line.Quantity,
                UnitPrice = line.BaseUnitPrice + line.ModifiersTotal,
                TaxAmount = priced.TaxAmount,
                LineTotal = priced.LineTotal,
                Modifiers = [.. line.Modifiers.Select(m => new OfflineOrderItemModifier { ModifierId = m.Id, PriceAdjustment = m.PriceAdjustment })],
            });
        }

        // Mirrors the server's createOrder/importOfflineOrder exactly: cash rounding
        // only ever applies to a single cash tender covering the whole sale. The
        // server independently recomputes and strictly validates this same total at
        // sync time (services/offlineSync.js), so this can't just be a display nicety
        // — it has to match bit for bit, or the sync gets rejected.
        var isSingleCashPayment = payments.Count == 1 && payments[0].Method == OfflinePaymentMethod.Cash;
        var effectiveUnit = isSingleCashPayment ? CashRoundingUnit : 0m;
        var roundedTotal = Money.RoundToNearest(totals.Total, effectiveUnit);
        var cashRoundingAdjustment = Money.Round(roundedTotal - totals.Total, DecimalDigits);

        return new OfflineOrderPayload
        {
            Id = id,
            LocationId = locationId,
            CashierId = cashierId,
            CashDrawerSessionId = cashDrawerSessionId,
            CustomerId = CustomerId,
            TerminalReceiptNo = terminalReceiptNo,
            CreatedAt = createdAt,
            Items = items,
            Discounts = DiscountAmount.HasValue && DiscountAmount.Value > 0
                ? [new OfflineOrderDiscount { Amount = DiscountAmount.Value, Reason = DiscountReason, PromotionCode = DiscountPromotionCode }]
                : [],
            Payments = [.. payments],
            Subtotal = totals.Subtotal,
            DiscountTotal = totals.DiscountTotal,
            TaxTotal = totals.TaxTotal,
            CashRoundingAdjustment = cashRoundingAdjustment,
            Total = roundedTotal,
        };
    }
}
