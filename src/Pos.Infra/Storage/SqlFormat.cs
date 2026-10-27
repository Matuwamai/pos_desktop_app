using System.Globalization;

namespace Pos.Infra.Storage;

/// <summary>The two primitive-to-SQLite conventions every table in this schema follows
/// (see <c>Migration0001Init</c>'s doc comment): money/quantity as invariant-culture TEXT,
/// never REAL, and booleans as 0/1 INTEGER. Shared so <c>SyncEngine</c> and
/// <c>SaleService</c> can't drift into formatting the same column differently.</summary>
public static class SqlFormat
{
    public static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static int Bool(bool value) => value ? 1 : 0;
}
