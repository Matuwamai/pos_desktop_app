using System.Text.Json;
using Pos.Core.Domain;
using Pos.Core.Sync;
using Pos.Infra.Json;

namespace Pos.Tests;

/// <summary>
/// Cart → OfflineOrderPayload, the conversion <see cref="Pos.Infra.Sales.SaleService"/>
/// sends to <c>sync/push</c>. Two things matter here that the pricing-math tests alone
/// don't cover: the payload's <c>unitPrice</c> is base price *plus* modifiers (see
/// <see cref="Cart.ToOfflineOrderPayload"/>'s doc comment — the server's offline path has
/// no separate modifiers field to add back in), and every field name on the wire has to
/// match <c>ORDER_PAYLOAD</c> in <c>server/services/offlineSync.js</c> exactly.
/// </summary>
public class CartTests
{
    private static Cart BuildCart()
    {
        var cart = new Cart { CustomerId = "customer-1", CustomerName = "Jane", DiscountAmount = 1.50m, DiscountReason = "loyalty" };
        cart.Items.Add(new CartLine
        {
            VariantId = "variant-1", ProductName = "Latte", Sku = "LATTE-001",
            BaseUnitPrice = 9.99m, Modifiers = [new CartModifier("mod-1", "Extra shot", 0.50m)], Quantity = 3, TaxRate = 0.16m,
        });
        cart.Items.Add(new CartLine
        {
            VariantId = "variant-2", ProductName = "Croissant", Sku = "CROI-001",
            BaseUnitPrice = 2.50m, Modifiers = [], Quantity = 1, TaxRate = null,
        });
        return cart;
    }

    private static OfflineOrderPayload BuildPayload(Cart cart) => cart.ToOfflineOrderPayload(
        id: "order-1", locationId: "location-1", cashierId: "cashier-1", cashDrawerSessionId: "drawer-1",
        terminalReceiptNo: "T1-20260101-0001", createdAt: DateTimeOffset.UtcNow,
        payments: [new OfflineOrderPayment { Method = "CASH", Amount = 100m }]);

    [Fact]
    public void Line_unitPrice_on_the_wire_includes_modifiers_not_just_the_base_price()
    {
        var payload = BuildPayload(BuildCart());

        var latteLine = payload.Items.Single(i => i.VariantId == "variant-1");
        Assert.Equal(9.99m + 0.50m, latteLine.UnitPrice); // base + modifier, matching the web cashier's own convention
    }

    [Fact]
    public void Every_line_satisfies_the_arithmetic_check_offlineSync_js_enforces()
    {
        var payload = BuildPayload(BuildCart());

        foreach (var item in payload.Items)
        {
            // Mirrors importOfflineOrder's own check exactly: lineSubtotal = round2(unitPrice
            // * quantity); lineSubtotal + taxAmount must equal lineTotal, or the sale is
            // rejected as tampered/corrupted.
            var lineSubtotal = Math.Round(item.UnitPrice * item.Quantity, 2, MidpointRounding.AwayFromZero);
            Assert.Equal(item.LineTotal, Math.Round(lineSubtotal + item.TaxAmount, 2, MidpointRounding.AwayFromZero));
        }
    }

    [Fact]
    public void Totals_match_the_sum_of_lines_and_the_discount()
    {
        var cart = BuildCart();
        var payload = BuildPayload(cart);

        var expectedSubtotal = payload.Items.Sum(i => Math.Round(i.UnitPrice * i.Quantity, 2, MidpointRounding.AwayFromZero));
        var expectedTax = payload.Items.Sum(i => i.TaxAmount);

        Assert.Equal(Math.Round(expectedSubtotal, 2), payload.Subtotal);
        Assert.Equal(Math.Round(expectedTax, 2), payload.TaxTotal);
        Assert.Equal(1.50m, payload.DiscountTotal);
        Assert.Equal(Math.Round(payload.Subtotal - payload.DiscountTotal + payload.TaxTotal, 2), payload.Total);
    }

    [Fact]
    public void Serialized_field_names_match_ORDER_PAYLOAD_exactly()
    {
        var json = JsonSerializer.Serialize(BuildPayload(BuildCart()), SyncJson.Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Top-level fields from server/services/offlineSync.js's ORDER_PAYLOAD.
        foreach (var field in new[] { "id", "locationId", "cashierId", "cashDrawerSessionId", "customerId", "terminalReceiptNo", "createdAt", "items", "discounts", "payments", "subtotal", "discountTotal", "taxTotal", "total" })
            Assert.True(root.TryGetProperty(field, out _), $"missing field: {field}");

        var item = root.GetProperty("items")[0];
        foreach (var field in new[] { "variantId", "quantity", "unitPrice", "taxAmount", "lineTotal", "modifiers" })
            Assert.True(item.TryGetProperty(field, out _), $"missing item field: {field}");

        var modifier = item.GetProperty("modifiers")[0];
        Assert.True(modifier.TryGetProperty("modifierId", out _));
        Assert.True(modifier.TryGetProperty("priceAdjustment", out _));

        var discount = root.GetProperty("discounts")[0];
        Assert.True(discount.TryGetProperty("amount", out _));
        Assert.True(discount.TryGetProperty("reason", out _));

        var payment = root.GetProperty("payments")[0];
        foreach (var field in new[] { "method", "amount" })
            Assert.True(payment.TryGetProperty(field, out _), $"missing payment field: {field}");
    }

    [Fact]
    public void No_optional_field_is_ever_written_as_a_literal_null()
    {
        // Same confirmed-live bug class as SyncJsonTests, replayed through the real
        // Cart -> payload path rather than a hand-built OfflineOrderPayload.
        var cart = BuildCart();
        cart.CustomerId = null; // the exact field that came back REJECTED in production before the fix
        var payload = BuildPayload(cart);

        var json = JsonSerializer.Serialize(payload, SyncJson.Options);
        Assert.DoesNotContain("customerId", json);
    }

    [Fact]
    public void AddOrIncrement_bumps_quantity_for_the_same_variant_and_modifier_selection()
    {
        var cart = new Cart();
        var line = new CartLine { VariantId = "v1", ProductName = "Latte", Sku = "LATTE-001", BaseUnitPrice = 5m, Modifiers = [], Quantity = 1 };
        cart.AddOrIncrement(line);
        cart.AddOrIncrement(new CartLine { VariantId = "v1", ProductName = "Latte", Sku = "LATTE-001", BaseUnitPrice = 5m, Modifiers = [], Quantity = 1 });

        Assert.Single(cart.Items);
        Assert.Equal(2, cart.Items[0].Quantity);
    }

    [Fact]
    public void AddOrIncrement_treats_a_different_modifier_selection_as_a_new_line()
    {
        var cart = new Cart();
        cart.AddOrIncrement(new CartLine { VariantId = "v1", ProductName = "Latte", Sku = "LATTE-001", BaseUnitPrice = 5m, Modifiers = [], Quantity = 1 });
        cart.AddOrIncrement(new CartLine { VariantId = "v1", ProductName = "Latte", Sku = "LATTE-001", BaseUnitPrice = 5m, Modifiers = [new CartModifier("m1", "Oat milk", 0.5m)], Quantity = 1 });

        Assert.Equal(2, cart.Items.Count);
    }
}
