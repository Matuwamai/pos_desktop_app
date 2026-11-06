using Pos.Core.Domain;
using Pos.Core.Pricing;
using Pos.Core.Sync;

namespace Pos.Tests;

public class CashRoundingTests
{
    [Theory]
    [InlineData("AUD", 0.05)]
    [InlineData("NZD", 0.10)]
    [InlineData("CAD", 0.05)]
    [InlineData("CHF", 0.05)]
    [InlineData("SEK", 1)]
    [InlineData("NOK", 1)]
    [InlineData("DKK", 0.50)]
    public void GetDefaultUnit_matches_the_documented_rounding_unit(string currency, decimal expected)
    {
        Assert.Equal(expected, CashRounding.GetDefaultUnit(currency));
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("KES")]
    public void GetDefaultUnit_is_zero_for_currencies_not_in_the_table(string currency)
    {
        Assert.Equal(0m, CashRounding.GetDefaultUnit(currency));
    }

    [Fact]
    public void GetEffectiveUnit_a_tenant_override_always_wins_over_the_currency_default()
    {
        Assert.Equal(0.05m, CashRounding.GetEffectiveUnit("AUD", null));
        Assert.Equal(0m, CashRounding.GetEffectiveUnit("AUD", 0m));
        Assert.Equal(0.10m, CashRounding.GetEffectiveUnit("AUD", 0.10m));
        Assert.Equal(0.05m, CashRounding.GetEffectiveUnit("USD", 0.05m));
    }

    [Theory]
    [InlineData(24.37, 0.05, 24.35)]
    [InlineData(24.38, 0.05, 24.40)]
    [InlineData(24.34, 0.10, 24.30)]
    [InlineData(24.35, 0.10, 24.40)] // midpoint rounds up
    [InlineData(23.24, 0.50, 23.00)]
    [InlineData(23.25, 0.50, 23.50)] // Denmark's own stated rule: 25 øre rounds up
    [InlineData(24.49, 1, 24)]
    [InlineData(24.50, 1, 25)]
    public void RoundToNearest(decimal amount, decimal unit, decimal expected)
    {
        Assert.Equal(expected, Money.RoundToNearest(amount, unit));
    }

    [Fact]
    public void RoundToNearest_a_zero_unit_is_a_no_op()
    {
        Assert.Equal(24.37m, Money.RoundToNearest(24.37m, 0m));
    }

    private static Cart MakeCart(decimal price, decimal cashRoundingUnit)
    {
        var cart = new Cart { CashRoundingUnit = cashRoundingUnit };
        cart.Items.Add(new CartLine { VariantId = "v1", ProductName = "Widget", Sku = "SKU-1", BaseUnitPrice = price, Modifiers = [], Quantity = 1 });
        return cart;
    }

    [Fact]
    public void ToOfflineOrderPayload_rounds_the_total_for_a_single_cash_payment()
    {
        var cart = MakeCart(24.37m, 0.05m);
        var payload = cart.ToOfflineOrderPayload("order-1", "loc-1", "cashier-1", null, "T1", DateTimeOffset.UtcNow,
            [new OfflineOrderPayment { Method = OfflinePaymentMethod.Cash, Amount = 24.35m }]);

        Assert.Equal(24.35m, payload.Total);
        Assert.Equal(-0.02m, payload.CashRoundingAdjustment);
        Assert.Equal(24.37m, payload.Subtotal); // subtotal/tax stay exact
    }

    [Fact]
    public void ToOfflineOrderPayload_does_not_round_a_split_cash_and_card_payment()
    {
        var cart = MakeCart(24.37m, 0.05m);
        var payload = cart.ToOfflineOrderPayload("order-2", "loc-1", "cashier-1", null, "T1", DateTimeOffset.UtcNow,
            [
                new OfflineOrderPayment { Method = OfflinePaymentMethod.Cash, Amount = 10m },
                new OfflineOrderPayment { Method = OfflinePaymentMethod.Card, Amount = 14.37m, TransactionRef = "ref" },
            ]);

        Assert.Equal(24.37m, payload.Total);
        Assert.Equal(0m, payload.CashRoundingAdjustment);
    }

    [Fact]
    public void ToOfflineOrderPayload_does_not_round_when_the_cart_has_no_rounding_unit_configured()
    {
        var cart = MakeCart(24.37m, 0m);
        var payload = cart.ToOfflineOrderPayload("order-3", "loc-1", "cashier-1", null, "T1", DateTimeOffset.UtcNow,
            [new OfflineOrderPayment { Method = OfflinePaymentMethod.Cash, Amount = 24.37m }]);

        Assert.Equal(24.37m, payload.Total);
        Assert.Equal(0m, payload.CashRoundingAdjustment);
    }
}
