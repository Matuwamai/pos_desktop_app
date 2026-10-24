using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Pos.Desktop.ViewModels;

/// <summary>XAML-friendly value converters for the sync status bar.</summary>
public static class BoolConverters
{
    public static readonly IValueConverter OnlineColor =
        new FuncValueConverter<bool, IBrush>(isOnline => isOnline ? Brushes.LimeGreen : Brushes.OrangeRed);
}
