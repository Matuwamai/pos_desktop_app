namespace Pos.Desktop.ViewModels;

/// <summary>One sellable variant as read from the local catalog tables, grouped under
/// its <see cref="CatalogProduct"/>.</summary>
public sealed record CatalogVariant(string VariantId, string Sku, decimal Price, decimal? TaxRate, decimal? Stock, bool TracksInventory);

public sealed record CatalogProduct(string ProductId, string Name, string? CategoryId, IReadOnlyList<string> ModifierGroupIds, IReadOnlyList<CatalogVariant> Variants)
{
    /// <summary>The web cashier's exact rule (<c>CatalogPane.jsx</c>'s <c>pickProduct</c>):
    /// a product with one variant and no modifier groups goes straight into the cart;
    /// anything else needs the variant/modifier picker.</summary>
    public bool NeedsPicker => Variants.Count != 1 || ModifierGroupIds.Count > 0;
}

public sealed record CategoryOption(string Id, string Name);

public sealed record ModifierOption(string Id, string Name, decimal PriceAdjustment);
public sealed record ModifierGroupOptions(string GroupId, string Name, int MinSelect, int MaxSelect, IReadOnlyList<ModifierOption> Options);

/// <summary>One cart line for display — rebuilt from <c>Pos.Core.Domain.Cart</c> after
/// every mutation rather than kept independently live-bound; cart sizes are small enough
/// that this is simpler than fine-grained per-property binding.</summary>
public sealed record CartLineRow(string Key, string ProductName, string ModifiersSummary, int Quantity, decimal LineSubtotal, decimal TaxAmount, decimal LineTotal);
