using System.Text.Json;
using Pos.Core.Sync;
using Pos.Infra.Json;

namespace Pos.Tests;

/// <summary>
/// Confirmed live, not hypothetical: before <c>SyncJson.Options</c> ignored null when
/// writing, every real sale from the desktop app came back <c>REJECTED</c> — the
/// server's zod schemas mark these fields <c>.optional()</c> (may be *absent*), not
/// <c>.nullable()</c>, so an explicit JSON <c>null</c> fails validation with "expected
/// string, received null." These tests pin that behaviour so it can't regress silently.
/// </summary>
public class SyncJsonTests
{
    [Fact]
    public void A_null_optional_field_is_omitted_not_written_as_null()
    {
        var payload = new OfflineOrderPayload
        {
            Id = "order-1",
            LocationId = "loc-1",
            CashierId = "cashier-1",
            CashDrawerSessionId = null, // optional in ORDER_PAYLOAD
            CustomerId = null, // optional in ORDER_PAYLOAD — the field that was actually rejected
            TerminalReceiptNo = "T1-20260101-0001",
            CreatedAt = DateTimeOffset.UtcNow,
            Items = [new OfflineOrderItem { VariantId = "v1", Quantity = 1, UnitPrice = 1, TaxAmount = 0, LineTotal = 1 }],
            Payments = [new OfflineOrderPayment { Method = "CASH", Amount = 1, TransactionRef = null }], // also rejected live
            Subtotal = 1,
            DiscountTotal = 0,
            TaxTotal = 0,
            Total = 1,
        };

        var json = JsonSerializer.Serialize(payload, SyncJson.Options);

        Assert.DoesNotContain("customerId", json);
        Assert.DoesNotContain("cashDrawerSessionId", json);
        Assert.DoesNotContain("transactionRef", json);
        Assert.DoesNotContain("null", json); // nothing in this payload should ever serialize as the literal null
    }
}
