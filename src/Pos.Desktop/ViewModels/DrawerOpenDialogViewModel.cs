using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Pos.Desktop.ViewModels;

/// <summary>
/// A hard gate before selling, ported from the web cashier's non-dismissible
/// <c>DrawerOpenModal</c> — there is no Cancel command here on purpose.
/// </summary>
public sealed partial class DrawerOpenDialogViewModel : ViewModelBase
{
    [ObservableProperty] private string _openingFloatText = "0.00";
    [ObservableProperty] private string? _errorMessage;

    public decimal? Result { get; private set; }
    public Action<bool>? RequestClose { get; set; }

    [RelayCommand]
    private void Open()
    {
        if (!decimal.TryParse(OpeningFloatText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0)
        {
            ErrorMessage = "Enter the opening float (0 is fine for no starting cash).";
            return;
        }
        Result = amount;
        RequestClose?.Invoke(true);
    }
}
