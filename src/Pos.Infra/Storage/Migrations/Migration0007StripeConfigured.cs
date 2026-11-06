namespace Pos.Infra.Storage.Migrations;

internal static class Migration0007StripeConfigured
{
    public const int Version = 7;

    public const string Sql = """
        ALTER TABLE tenant ADD COLUMN stripe_configured INTEGER DEFAULT 0;
        """;
}
