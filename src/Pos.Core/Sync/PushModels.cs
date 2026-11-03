namespace Pos.Core.Sync;

// Mirrors `validations/sync.js` (push body) and the `ORDER_PAYLOAD`/`DRAWER_OPEN`/
// `DRAWER_CLOSE` zod schemas in `services/offlineSync.js`. One `SyncOp` = one outbox
// row; `OpId` is the idempotency key the server's `SyncOp` table is keyed on, so it
// must be assigned once, locally, when the sale/drawer action happens — never
// regenerated on retry.

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

/// <summary>The outbox wire format: an opaque <see cref="Payload"/> keyed by <see cref="Type"/>,
/// so new op types don't need a new endpoint.</summary>
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

/// <summary><c>Status</c> is one of <c>CREATED</c>, <c>DUPLICATE</c> (replay — safe to drop
/// from the outbox either way) or <c>REJECTED</c> (a real error — see <c>Result.error</c>;
/// the op stays in the outbox for the cashier/support to look at, it is not retried as-is).</summary>
public sealed class SyncOpResult
{
    public string OpId { get; set; } = "";
    public string Status { get; set; } = "";
    public System.Text.Json.JsonElement Result { get; set; }
}

/// <summary>Payload for <see cref="SyncOpType.OrderCreate"/> — matches <c>ORDER_PAYLOAD</c> in
/// <c>server/services/offlineSync.js</c> exactly (field names, required-ness). The server does
/// not reprice this: it only checks the arithmetic is internally consistent.</summary>
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

    /// <summary>Carried alongside <see cref="Amount"/> (never in place of it) purely so the
    /// server can re-validate the promotion is *still* active/in-window at the moment the
    /// sale actually syncs — the till already computed <see cref="Amount"/> itself, from a
    /// live preview, before the cashier applied it. See
    /// <c>server/services/offlineSync.js</c>'s <c>ORDER_PAYLOAD</c> discount schema.</summary>
    public string? PromotionCode { get; set; }
}

/// <summary><c>GiftCard</c>/<c>StoreCredit</c> redemption needs a fresh, in-transaction balance
/// check to avoid double-spending the same card/balance across two offline tills — the server
/// now does that check at sync time (see <c>server/services/offlineSync.js</c>), but these two
/// methods are still only ever *offered* to the cashier while the till is currently online
/// (<c>PaymentDialogViewModel.Methods</c>), the same gating M-Pesa STK push already uses.</summary>
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

/// <summary>Payload for <see cref="SyncOpType.CustomerCreate"/> — matches the
/// <c>CUSTOMER_CREATE</c> zod schema in <c>server/services/offlineSync.js</c>. Pushed
/// synchronously, not queued through the outbox like the other op types: customer
/// creation is online-only in v1 (see <c>SyncEngine.CreateCustomerAsync</c>), and the
/// cashier needs the real server-assigned id back immediately to attach it to the sale
/// in progress.</summary>
public sealed class CustomerCreatePayload
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
}
