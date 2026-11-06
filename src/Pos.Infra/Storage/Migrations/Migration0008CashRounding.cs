namespace Pos.Infra.Storage.Migrations;

internal static class Migration0008CashRounding
{
    public const int Version = 8;

    public const string Sql = """
        ALTER TABLE tenant ADD COLUMN cash_rounding_unit TEXT;
        """;
}
