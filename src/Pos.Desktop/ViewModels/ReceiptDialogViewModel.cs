using CommunityToolkit.Mvvm.Input;
using Pos.Core.Interfaces;

namespace Pos.Desktop.ViewModels;

/// <summary>
/// On-screen only — no <see cref="IReceiptPrinter"/> implementation exists yet (that's
/// Phase 3) and the order may not even exist server-side yet if this sale happened
/// offline, so there's nothing to fetch a server-rendered receipt from either. Built
/// straight from the local sale, which is also the shape Phase 3's printer will consume.
/// </summary>
public sealed partial class ReceiptDialogViewModel(ReceiptDocument receipt) : ViewModelBase
{
    public ReceiptDocument Receipt { get; } = receipt;

    public Action? RequestClose { get; set; }

    [RelayCommand]
    private void NewSale() => RequestClose?.Invoke();
}
