namespace Pos.Core.Sync;

public static class SyncOpType
{
    public const string OrderCreate = "order.create";
    public const string DrawerOpen = "drawer.open";
    public const string DrawerClose = "drawer.close";
    public const string CustomerCreate = "customer.create";
}

public sealed class PushRequest
{
    public List<SyncOpEnvelope> Ops { get; set; } = [];
}
public sealed class SyncOpEnvelope
{
    public string OpId { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTimeOffset? ClientTime { get; set; }
    public object Payload { get; set; } = new();
}

public sealed class PushResponse
{
    public List<SyncOpResult> Results { get; set; } = [];
}
public sealed class SyncOpResult
{
    public string OpId { get; set; } = "";
    public string Status { get; set; } = "";
    public System.Text.Json.JsonElement Result { get; set; }
}
public sealed class OfflineOrderPayload
{
    public string Id { get; set; } = "";
    public string LocationId { get; set; } = "";
    public string CashierId { get; set; } = "";
    public string? CashDrawerSessionId { get; set; }
    public string? CustomerId { get; set; }
    public string TerminalReceiptNo { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<OfflineOrderItem> Items { get; set; } = [];
    public List<OfflineOrderDiscount> Discounts { get; set; } = [];
    public List<OfflineOrderPayment> Payments { get; set; } = [];
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
}

public sealed class OfflineOrderItem
{
    public string VariantId { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public List<OfflineOrderItemModifier> Modifiers { get; set; } = [];
}

public sealed class OfflineOrderItemModifier
{
    public string ModifierId { get; set; } = "";
    public decimal PriceAdjustment { get; set; }
}

public sealed class OfflineOrderDiscount
{
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public string? PromotionCode { get; set; }
}

public static class OfflinePaymentMethod
{
    public const string Cash = "CASH";
    public const string Card = "CARD";
    public const string Mpesa = "MPESA";
    public const string GiftCard = "GIFT_CARD";
    public const string StoreCredit = "STORE_CREDIT";
    public const string Other = "OTHER";
}

public sealed class OfflineOrderPayment
{
    public string Method { get; set; } = OfflinePaymentMethod.Cash;
    public decimal Amount { get; set; }
    public string? TransactionRef { get; set; }

    /// <summary>Required by the server when <see cref="Method"/> is <see cref="OfflinePaymentMethod.GiftCard"/>.</summary>
    public string? GiftCardCode { get; set; }
}

public sealed class DrawerOpenPayload
{
    public string Id { get; set; } = "";
    public string LocationId { get; set; } = "";
    public string CashierId { get; set; } = "";
    public decimal OpeningFloat { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
}

public sealed class DrawerClosePayload
{
    public string Id { get; set; } = "";
    public string CashierId { get; set; } = "";
    public decimal ClosingCash { get; set; }
    public DateTimeOffset ClosedAt { get; set; }
}

public sealed class CustomerCreatePayload
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
}
