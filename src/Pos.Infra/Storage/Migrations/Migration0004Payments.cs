namespace Pos.Infra.Storage.Migrations;

/// <summary>
/// Phase 4: the tenant's till/paybill number, synced down alongside its name/logo
/// (see <c>Migration0003Printer</c>'s <c>tenant</c> table) so a cashier can see where
/// to tell a customer to send M-Pesa payment even while offline. No Daraja API
/// credentials are involved — see the Phase 4 plan: this is manual confirmation only,
/// the cashier types the SMS code in as the payment's transactionRef.
/// </summary>
internal static class Migration0004Payments
{
    public const int Version = 4;

    public const string Sql = """
        ALTER TABLE tenant ADD COLUMN mpesa_type TEXT;
        ALTER TABLE tenant ADD COLUMN mpesa_shortcode TEXT;
        """;
}
