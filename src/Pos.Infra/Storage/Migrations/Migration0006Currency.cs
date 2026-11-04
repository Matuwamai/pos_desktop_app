namespace Pos.Infra.Storage.Migrations;

/// <summary>
/// The tenant's currency (ISO 4217), set once at signup and immutable after — see
/// <c>server/prisma/schema.prisma</c>'s <c>Tenant.currency</c>. Defaults existing
/// cached rows to USD on upgrade, same neutral backfill the server's own migration used.
/// </summary>
internal static class Migration0006Currency
{
    public const int Version = 6;

    public const string Sql = """
        ALTER TABLE tenant ADD COLUMN currency TEXT NOT NULL DEFAULT 'USD';
        """;
}
