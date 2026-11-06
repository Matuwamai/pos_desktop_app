namespace Pos.Infra.Storage.Migrations;

internal static class Migration0009ReceiptSettings
{
    public const int Version = 9;

    public const string Sql = """
        ALTER TABLE tenant ADD COLUMN receipt_footer_message TEXT;
        ALTER TABLE tenant ADD COLUMN receipt_show_customer_name INTEGER NOT NULL DEFAULT 1;
        ALTER TABLE tenant ADD COLUMN receipt_show_cashier_name INTEGER NOT NULL DEFAULT 1;
        ALTER TABLE tenant ADD COLUMN receipt_show_logo INTEGER NOT NULL DEFAULT 1;
        """;
}
