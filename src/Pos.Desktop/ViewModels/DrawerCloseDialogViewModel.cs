using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Pos.Desktop.ViewModels;

public sealed partial class DrawerCloseDialogViewModel : ViewModelBase
{
    [ObservableProperty] private string _closingCashText = "0.00";
    [ObservableProperty] private string? _errorMessage;

    public decimal? Result { get; private set; }
    public Action<bool>? RequestClose { get; set; }

    [RelayCommand]
    private void Close()
    {
        if (!decimal.TryParse(ClosingCashText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0)
        {
            ErrorMessage = "Enter the cash counted in the drawer.";
            return;
        }
        Result = amount;
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
