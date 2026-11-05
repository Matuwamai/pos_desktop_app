namespace Pos.Infra.Storage.Migrations;

internal static class Migration0006Currency
{
    public const int Version = 6;

    public const string Sql = """
        ALTER TABLE tenant ADD COLUMN currency TEXT NOT NULL DEFAULT 'USD';
        """;
}
