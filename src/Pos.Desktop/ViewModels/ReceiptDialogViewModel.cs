using CommunityToolkit.Mvvm.Input;
using Pos.Core.Interfaces;

namespace Pos.Desktop.ViewModels;


public sealed partial class ReceiptDialogViewModel(ReceiptDocument receipt) : ViewModelBase
{
    public ReceiptDocument Receipt { get; } = receipt;

    public Action? RequestClose { get; set; }

    [RelayCommand]
    private void NewSale() => RequestClose?.Invoke();
}
