using System.Globalization;

namespace Pos.Desktop.ViewModels;


public sealed record CatalogVariant(string VariantId, string Sku, decimal Price, decimal? TaxRate, decimal? Stock, bool TracksInventory, int DecimalDigits = 2)
{
    public string PriceText => Price.ToString(DecimalDigits == 0 ? "0" : "0." + new string('0', DecimalDigits), CultureInfo.InvariantCulture);
}

public sealed record CatalogProduct(string ProductId, string Name, string? CategoryId, IReadOnlyList<string> ModifierGroupIds, IReadOnlyList<CatalogVariant> Variants)
{

    public bool NeedsPicker => Variants.Count != 1 || ModifierGroupIds.Count > 0;
}

public sealed record CategoryOption(string Id, string Name);

public sealed record ModifierOption(string Id, string Name, decimal PriceAdjustment);
public sealed record ModifierGroupOptions(string GroupId, string Name, int MinSelect, int MaxSelect, IReadOnlyList<ModifierOption> Options);


public sealed record CartLineRow(string Key, string ProductName, string ModifiersSummary, int Quantity, decimal LineSubtotal, decimal TaxAmount, decimal LineTotal, int DecimalDigits = 2)
{
    public string LineTotalText => LineTotal.ToString(DecimalDigits == 0 ? "0" : "0." + new string('0', DecimalDigits), CultureInfo.InvariantCulture);
}
