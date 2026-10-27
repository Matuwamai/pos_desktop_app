using Pos.Core.Pricing;
using Pos.Core.Sync;

namespace Pos.Core.Domain;

public sealed record CartModifier(string Id, string Name, decimal PriceAdjustment);

/// <summary>One cart line: a variant plus a fixed modifier selection. Adding the same
/// variant+modifier combination again should bump <see cref="Quantity"/> rather than add
/// a second line — <see cref="Key"/> is what a caller dedupes on, matching the web
/// cashier's cart slice (<c>posSlice.js</c>: <c>${variantId}::${sortedModifierIds}</c>).</summary>
public sealed class CartLine
{
    public required string VariantId { get; init; }
    public required string ProductName { get; init; }
    public required string Sku { get; init; }

    /// <summary>The variant's own price — modifiers are tracked separately in
    /// <see cref="Modifiers"/> and summed via <see cref="ModifiersTotal"/>. Not the same
    /// as the wire payload's <c>unitPrice</c> — see <see cref="Cart.ToOfflineOrderPayload"/>.</summary>
    public required decimal BaseUnitPrice { get; init; }
    public required IReadOnlyList<CartModifier> Modifiers { get; init; }
    public required int Quantity { get; set; }
    public decimal? TaxRate { get; init; }

    public decimal ModifiersTotal => Modifiers.Sum(m => m.PriceAdjustment);

    public string Key => VariantId + "::" + string.Join(",", Modifiers.Select(m => m.Id).OrderBy(id => id, StringComparer.Ordinal));
}

/// <summary>
/// An in-progress sale: lines, an optional attached customer, and at most one discount
/// (manual amount only — see <c>PricingMath.DiscountInput</c>'s doc comment for why promo
/// codes aren't resolved here). Pricing always goes through <see cref="PricingMath"/>, the
/// same decimal port of the server's <c>round2()</c> math that Pos.Core's golden-fixture
/// tests already prove agrees with the server to the cent.
/// </summary>
public sealed class Cart
{
    public List<CartLine> Items { get; } = [];
    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public decimal? DiscountAmount { get; set; }
    public string? DiscountReason { get; set; }

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
    }

    public IReadOnlyList<PricedLine> PriceLines() =>
        [.. Items.Select(line => PricingMath.PriceLine(line.BaseUnitPrice, line.ModifiersTotal, line.Quantity, line.TaxRate))];

    public IReadOnlyList<DiscountInput> Discounts() =>
        DiscountAmount.HasValue && DiscountAmount.Value > 0 ? [new DiscountInput(DiscountAmount.Value)] : [];

    public OrderTotals ComputeTotals() => PricingMath.ComputeTotals(PriceLines(), Discounts());

    /// <summary>
    /// Builds the exact push DTO for <c>sync/push</c>'s <c>order.create</c> op. Note the
    /// wire payload's <c>unitPrice</c> is <see cref="CartLine.BaseUnitPrice"/> **plus**
    /// modifiers, not the base price alone: <c>server/services/offlineSync.js</c>'s
    /// <c>importOfflineOrder</c> recomputes <c>lineSubtotal = round2(unitPrice * quantity)</c>
    /// with no separate modifiers field to add in (unlike the *online* path in
    /// <c>order.js</c>'s <c>priceAndValidateItem</c>, which keeps them separate) — so the
    /// combined price has to already be baked in before it's sent, same as the web
    /// cashier does at add-to-cart time.
    /// </summary>
    public OfflineOrderPayload ToOfflineOrderPayload(
        string id, string locationId, string cashierId, string? cashDrawerSessionId,
        string terminalReceiptNo, DateTimeOffset createdAt, IReadOnlyList<OfflineOrderPayment> payments)
    {
        var totals = ComputeTotals();
        var items = new List<OfflineOrderItem>(Items.Count);
        foreach (var line in Items)
        {
            var priced = PricingMath.PriceLine(line.BaseUnitPrice, line.ModifiersTotal, line.Quantity, line.TaxRate);
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
                ? [new OfflineOrderDiscount { Amount = DiscountAmount.Value, Reason = DiscountReason }]
                : [],
            Payments = [.. payments],
            Subtotal = totals.Subtotal,
            DiscountTotal = totals.DiscountTotal,
            TaxTotal = totals.TaxTotal,
            Total = totals.Total,
        };
    }
}
