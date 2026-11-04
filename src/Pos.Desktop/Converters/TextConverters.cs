using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Pos.Desktop.ViewModels;

public static class TextConverters
{
    public static readonly IValueConverter Initial =
        new FuncValueConverter<string?, string>(name =>
            string.IsNullOrWhiteSpace(name) ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString());


    public static readonly IValueConverter PinDots =
        new FuncValueConverter<string?, string>(pin =>
        {
            var len = Math.Min(pin?.Length ?? 0, 8);
            return string.Join("  ", Enumerable.Range(0, 8).Select(i => i < len ? "●" : "○"));
        });

    public static readonly IMultiValueConverter CartQuantityBadge =
        new FuncMultiValueConverter<object?, string>(values =>
        {
            var list = values.ToList();
            if (list.Count < 3 || list[0] is not CatalogProduct product || list[2] is not CashierViewModel vm) return "";
            var qty = vm.QuantityInCart(product);
            return qty > 0 ? qty.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        });

    private static readonly IReadOnlyList<IBrush> ProductPalette =
    [
        new SolidColorBrush(Color.Parse("#C7D2FE")), // indigo-ish
        new SolidColorBrush(Color.Parse("#FBCFE8")), // pink-ish
        new SolidColorBrush(Color.Parse("#BBF7D0")), // green-ish
        new SolidColorBrush(Color.Parse("#FDE68A")), // amber-ish
    ];

    public static readonly IValueConverter ProductColor =
        new FuncValueConverter<string?, IBrush>(id =>
            ProductPalette[Math.Abs((id ?? "").GetHashCode()) % ProductPalette.Count]);
}
