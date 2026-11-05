namespace Pos.Infra.Storage.Migrations;

internal static class Migration0005MpesaStk
{
    public const int Version = 5;

    public const string Sql = """
        ALTER TABLE tenant ADD COLUMN mpesa_stk_configured INTEGER DEFAULT 0;
        """;
}
