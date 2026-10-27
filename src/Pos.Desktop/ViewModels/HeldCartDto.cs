using Pos.Core.Domain;

namespace Pos.Desktop.ViewModels;

/// <summary>
/// A plain, explicit JSON shape for a held sale's serialized cart — not <c>Cart</c>/
/// <c>CartLine</c> directly. Those use <c>required</c> init-only members and an
/// <c>IReadOnlyList&lt;CartModifier&gt;</c> property, which System.Text.Json can
/// round-trip, but stacking all of that reflection-based behavior is more fragile than
/// writing the ten lines of explicit mapping below.
/// </summary>
internal sealed class HeldCartDto
{
    public List<HeldCartLineDto> Items { get; set; } = [];
    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public decimal? DiscountAmount { get; set; }
    public string? DiscountReason { get; set; }

    public static HeldCartDto FromCart(Cart cart) => new()
    {
        Items = [.. cart.Items.Select(HeldCartLineDto.FromCartLine)],
        CustomerId = cart.CustomerId,
        CustomerName = cart.CustomerName,
        DiscountAmount = cart.DiscountAmount,
        DiscountReason = cart.DiscountReason,
    };

    public Cart ToCart()
    {
        var cart = new Cart { CustomerId = CustomerId, CustomerName = CustomerName, DiscountAmount = DiscountAmount, DiscountReason = DiscountReason };
        cart.Items.AddRange(Items.Select(i => i.ToCartLine()));
        return cart;
    }
}

internal sealed class HeldCartLineDto
{
    public string VariantId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Sku { get; set; } = "";
    public decimal BaseUnitPrice { get; set; }
    public List<CartModifier> Modifiers { get; set; } = [];
    public int Quantity { get; set; }
    public decimal? TaxRate { get; set; }

    public static HeldCartLineDto FromCartLine(CartLine line) => new()
    {
        VariantId = line.VariantId,
        ProductName = line.ProductName,
        Sku = line.Sku,
        BaseUnitPrice = line.BaseUnitPrice,
        Modifiers = [.. line.Modifiers],
        Quantity = line.Quantity,
        TaxRate = line.TaxRate,
    };

    public CartLine ToCartLine() => new()
    {
        VariantId = VariantId,
        ProductName = ProductName,
        Sku = Sku,
        BaseUnitPrice = BaseUnitPrice,
        Modifiers = Modifiers,
        Quantity = Quantity,
        TaxRate = TaxRate,
    };
}
