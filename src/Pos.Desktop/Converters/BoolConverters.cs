using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Pos.Desktop.ViewModels;


public static class BoolConverters
{
    public static readonly IValueConverter OnlineColor =
        new FuncValueConverter<bool, IBrush>(isOnline => isOnline ? Brushes.LimeGreen : Brushes.OrangeRed);

    private static readonly IBrush StepAccent = new SolidColorBrush(Color.Parse("#4F46E5"));
    private static readonly IBrush StepMuted = new SolidColorBrush(Color.Parse("#78716C"));

    public static readonly IValueConverter StepFill =
        new FuncValueConverter<bool, IBrush>(done => done ? StepAccent : StepMuted);
}
