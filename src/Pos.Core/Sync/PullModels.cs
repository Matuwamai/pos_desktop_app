using System.Text.Json;

namespace Pos.Core.Sync;

public sealed class PullResponse
{
    public long ServerTime { get; set; }
    public long NextSince { get; set; }
    public bool HasMore { get; set; }
    public int Limit { get; set; }
    public PullEntities Entities { get; set; } = new();
}

public sealed class PullEntities
{
    public List<LocationRow> Locations { get; set; } = [];
    public List<CategoryRow> Categories { get; set; } = [];
    public List<ProductRow> Products { get; set; } = [];
    public List<VariantRow> Variants { get; set; } = [];
    public List<ModifierGroupRow> ModifierGroups { get; set; } = [];
    public List<ModifierRow> Modifiers { get; set; } = [];
    public List<TaxRateRow> TaxRates { get; set; } = [];
    public List<PromotionRow> Promotions { get; set; } = [];
    public List<CustomerGroupRow> CustomerGroups { get; set; } = [];
    public List<CustomerRow> Customers { get; set; } = [];
    public List<InventoryRow> Inventory { get; set; } = [];
    public List<RosterUserRow> Users { get; set; } = [];
    public TenantRow? Tenant { get; set; }
}
public sealed class TenantRow
{
    public string Name { get; set; } = "";
    // ISO 4217, set once at signup and immutable after — see server/config/currencies.js.
    public string Currency { get; set; } = "USD";
    public string? LogoUrl { get; set; }
    public string? MpesaType { get; set; } // "TILL" | "PAYBILL" | null (not set up yet)
    public string? MpesaShortcode { get; set; }
    // Whether the tenant has Daraja credentials configured server-side — never the
    // credentials themselves, which never cross the sync wire at all.
    public bool MpesaStkConfigured { get; set; }
    // Whether the tenant has a Stripe secret key configured server-side — never the key
    // itself, which never crosses the sync wire either.
    public bool StripeConfigured { get; set; }
    // Already resolved server-side (tenant's own override, else the currency's
    // default) — see services/tenantSelf.js's present() / services/sync.js.
    public decimal EffectiveCashRoundingUnit { get; set; }
    // null means "use the hardcoded fallback" — see EscPosEncoder.cs.
    public string? ReceiptFooterMessage { get; set; }
    public bool ReceiptShowCustomerName { get; set; } = true;
    public bool ReceiptShowCashierName { get; set; } = true;
    public bool ReceiptShowLogo { get; set; } = true;
    public string UpdatedAt { get; set; } = "";
}

public sealed class LocationRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Timezone { get; set; } = "";
    public bool IsActive { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public sealed class CategoryRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentId { get; set; }
    public bool IsActive { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public sealed class ProductRow
{
    public string Id { get; set; } = "";
    public string? CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsComposite { get; set; }
    public bool TrackInventory { get; set; }
    public bool IsActive { get; set; }
    public string UpdatedAt { get; set; } = "";
    public List<string> ModifierGroupIds { get; set; } = [];
}

public sealed class VariantRow
{
    public string Id { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string Sku { get; set; } = "";
    public string? Barcode { get; set; }
    public JsonElement Attributes { get; set; }
    public decimal Price { get; set; }
    public decimal? Cost { get; set; } // nullable in the schema too — "for margin reporting", not every variant has one
    public string? TaxRateId { get; set; }
    public bool IsActive { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public sealed class ModifierGroupRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int MinSelect { get; set; }
    public int MaxSelect { get; set; }
    public bool IsActive { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public sealed class ModifierRow
{
    public string Id { get; set; } = "";
    public string ModifierGroupId { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PriceAdjustment { get; set; }
    public bool IsActive { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public sealed class TaxRateRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Rate { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public sealed class PromotionRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string Type { get; set; } = "";
    public decimal Value { get; set; }
    public decimal? MinSubtotal { get; set; }
    public string? StartsAt { get; set; }
    public string? EndsAt { get; set; }
    public bool IsActive { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public sealed class CustomerGroupRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal DiscountPercentage { get; set; }
    public bool IsActive { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public sealed class CustomerRow
{
    public string Id { get; set; } = "";
    public string? CustomerGroupId { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public decimal StoreCreditBalance { get; set; }
    public bool IsActive { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public sealed class InventoryRow
{
    public string Id { get; set; } = "";
    public string VariantId { get; set; } = "";
    public string LocationId { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal? LowStockThreshold { get; set; }
    public string UpdatedAt { get; set; } = "";
}
public sealed class RosterUserRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
    public string? PinHash { get; set; }
    public bool MustChangePassword { get; set; }
    public string UpdatedAt { get; set; } = "";
}
