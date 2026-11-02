using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Pos.Desktop.ViewModels;

/// <summary>XAML-friendly value converters for the sync status bar.</summary>
public static class BoolConverters
{
    public static readonly IValueConverter OnlineColor =
        new FuncValueConverter<bool, IBrush>(isOnline => isOnline ? Brushes.LimeGreen : Brushes.OrangeRed);

    private static readonly IBrush StepAccent = new SolidColorBrush(Color.Parse("#4F46E5"));
    private static readonly IBrush StepMuted = new SolidColorBrush(Color.Parse("#78716C"));

    /// <summary>Setup wizard step indicator: true (this step reached/passed) -> accent,
    /// false -> muted. Hardcoded to match App.axaml's AccentBrush/MutedBrush since a
    /// converter can't reference XAML StaticResources.</summary>
    public static readonly IValueConverter StepFill =
        new FuncValueConverter<bool, IBrush>(done => done ? StepAccent : StepMuted);
}
