using System.Globalization;

namespace Pos.Infra.Storage;

public static class SqlFormat
{
    public static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static int Bool(bool value) => value ? 1 : 0;
}
