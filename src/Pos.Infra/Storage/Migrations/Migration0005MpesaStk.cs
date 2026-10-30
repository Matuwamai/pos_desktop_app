namespace Pos.Infra.Storage.Migrations;

/// <summary>
/// Phase 4b: whether the tenant has Daraja (M-Pesa STK push) credentials
/// configured server-side — just the boolean, never the credentials
/// themselves, which never cross the sync wire at all (see
/// <c>Pos.Core.Sync.TenantRow.MpesaStkConfigured</c>).
/// </summary>
internal static class Migration0005MpesaStk
{
    public const int Version = 5;

    public const string Sql = """
        ALTER TABLE tenant ADD COLUMN mpesa_stk_configured INTEGER DEFAULT 0;
        """;
}
