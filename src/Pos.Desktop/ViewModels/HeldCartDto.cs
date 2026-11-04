using Pos.Core.Domain;

namespace Pos.Desktop.ViewModels;


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
