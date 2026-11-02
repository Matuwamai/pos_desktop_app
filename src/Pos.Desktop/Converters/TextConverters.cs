using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Pos.Desktop.ViewModels;

/// <summary>XAML-friendly value converters for small text transforms used across the
/// restyled views (avatar initials, the PIN dot/box display).</summary>
public static class TextConverters
{
    public static readonly IValueConverter Initial =
        new FuncValueConverter<string?, string>(name =>
            string.IsNullOrWhiteSpace(name) ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString());

    /// <summary>Renders a PIN as a fixed-width row of filled/unfilled dots instead of
    /// the raw characters — this is a read-only status display, not an editable field,
    /// so it never shows how many digits beyond a sensible max (8, same as mobile).</summary>
    public static readonly IValueConverter PinDots =
        new FuncValueConverter<string?, string>(pin =>
        {
            var len = Math.Min(pin?.Length ?? 0, 8);
            return string.Join("  ", Enumerable.Range(0, 8).Select(i => i < len ? "●" : "○"));
        });

    /// <summary>Catalog card quantity badge. Bound as a MultiBinding: [0] the
    /// CatalogProduct itself, [1] CartRows.Count (just a change trigger — any cart
    /// edit re-evaluates this), [2] the CashierViewModel (to call QuantityInCart on).
    /// Defensive: any unexpected shape just shows nothing rather than throwing.</summary>
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

    /// <summary>Deterministic per-product color standing in for a product image,
    /// same idea as the mobile app's catalog card — picked from the product's own id
    /// so the same product always gets the same color across a session.</summary>
    public static readonly IValueConverter ProductColor =
        new FuncValueConverter<string?, IBrush>(id =>
            ProductPalette[Math.Abs((id ?? "").GetHashCode()) % ProductPalette.Count]);
}
