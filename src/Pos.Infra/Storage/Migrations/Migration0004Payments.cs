namespace Pos.Infra.Storage.Migrations;

internal static class Migration0004Payments
{
    public const int Version = 4;

    public const string Sql = """
        ALTER TABLE tenant ADD COLUMN mpesa_type TEXT;
        ALTER TABLE tenant ADD COLUMN mpesa_shortcode TEXT;
        """;
}
