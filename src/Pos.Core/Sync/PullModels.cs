using System.Text.Json;

namespace Pos.Core.Sync;

// Mirrors the JSON shapes `services/sync.js` (`pullChanges`) and
// `routers/sync.js` (`GET /sync/pull`) produce. Every row carries `UpdatedAt` —
// the cursor field — and the server's own tombstone model is `isActive: false`,
// not a delete, so these are plain upserts, never row removal.

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

    /// <summary>Singleton, not a list — this terminal's own tenant. Null only if the
    /// tenant row itself is somehow missing server-side (shouldn't happen in practice).</summary>
    public TenantRow? Tenant { get; set; }
}

/// <summary>Always sent in full on every pull (not filtered by "changed since") — one
/// row, not worth a cursor of its own. See <c>services/sync.js</c>'s comment on why.</summary>
public sealed class TenantRow
{
    public string Name { get; set; } = "";
    public string? LogoUrl { get; set; }
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

/// <summary>The offline-login roster: permission codes already resolved server-side, and a
/// bcrypt PIN hash (never the password hash) — the till never recomputes permissions itself.</summary>
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
