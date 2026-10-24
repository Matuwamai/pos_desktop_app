namespace Pos.Desktop.ViewModels;

/// <summary>One row of the read-only catalog list — one variant, joined with its
/// product name and this location's stock. This is the Phase 1 "proof of pull"
/// screen, not the POS cashier screen (that's Phase 2, on the same local tables).</summary>
public sealed record ProductRowViewModel(string VariantId, string ProductName, string Sku, decimal Price, decimal? StockQuantity, bool TracksInventory);
