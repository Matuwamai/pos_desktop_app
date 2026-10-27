using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Pos.Core.Domain;
using Pos.Core.Interfaces;
using Pos.Core.Sync;
using Pos.Desktop.ViewModels;
using Pos.Infra.Storage;
using Pos.Infra.Sync;

namespace Pos.Desktop.Views;

/// <summary>
/// Wires <see cref="CashierViewModel"/>'s <c>Show*</c> hooks to real dialog windows, the
/// window-level F2/F4 shortcuts, and Enter-in-the-search-box (barcode scan support — see
/// <c>SearchEnterCommand</c>'s doc comment), all ported from <c>PosCashierPage.jsx</c>.
/// This is the one place this screen touches Avalonia's windowing types directly — the
/// ViewModel never does.
/// </summary>
public partial class CashierView : UserControl
{
    public CashierView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireViewModel();
        AttachedToVisualTree += OnAttachedToVisualTree;
        SearchBox.KeyDown += OnSearchBoxKeyDown;
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not CashierViewModel vm) return;
        if (vm.SearchEnterCommand.CanExecute(null)) vm.SearchEnterCommand.Execute(null);
        e.Handled = true;
    }

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (Owner is { } window) window.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CashierViewModel vm) return;
        if (e.Key == Key.F2)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.F4)
        {
            if (vm.PayCommand.CanExecute(null)) vm.PayCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void WireViewModel()
    {
        if (DataContext is not CashierViewModel vm) return;

        vm.ShowVariantPicker = async (product, groups) =>
        {
            var dialogVm = new VariantModifierDialogViewModel(product, groups);
            var dialog = new VariantModifierDialog(dialogVm);
            await dialog.ShowDialog(Owner!);
            return dialogVm.Result;
        };

        vm.ShowDiscountDialog = async (role, amount, reason) =>
        {
            var dialogVm = new DiscountDialogViewModel(role, amount, reason);
            var dialog = new DiscountDialog(dialogVm);
            var applied = await dialog.ShowDialog<bool>(Owner!);
            return (applied, dialogVm.ResultAmount ?? 0, dialogVm.ResultReason);
        };

        vm.ShowCustomerDialog = async isOnline =>
        {
            var dialogVm = new CustomerDialogViewModel(GetDb(vm), GetSync(vm), GetDeviceToken(vm), isOnline);
            var dialog = new CustomerDialog(dialogVm);
            var applied = await dialog.ShowDialog<bool>(Owner!);
            return (applied, dialogVm.Result?.Id ?? "", dialogVm.Result?.Name ?? "");
        };

        vm.ShowPaymentDialog = async total =>
        {
            var dialogVm = new PaymentDialogViewModel(total);
            var dialog = new PaymentDialog(dialogVm);
            await dialog.ShowDialog(Owner!);
            return dialogVm.Result;
        };

        vm.ShowDrawerOpenDialog = async () =>
        {
            var dialogVm = new DrawerOpenDialogViewModel();
            var dialog = new DrawerOpenDialog(dialogVm);
            await dialog.ShowDialog(Owner!);
            return dialogVm.Result;
        };

        vm.ShowDrawerCloseDialog = async () =>
        {
            var dialogVm = new DrawerCloseDialogViewModel();
            var dialog = new DrawerCloseDialog(dialogVm);
            var confirmed = await dialog.ShowDialog<bool>(Owner!);
            return confirmed ? dialogVm.Result : null;
        };

        vm.ShowReceiptDialog = async receipt =>
        {
            var dialogVm = new ReceiptDialogViewModel(receipt);
            var dialog = new ReceiptDialog(dialogVm);
            await dialog.ShowDialog(Owner!);
        };
    }

    // CashierViewModel's db/sync/deviceToken fields are private — these small reflection-free
    // accessors would normally just be constructor parameters passed straight through, but
    // CustomerDialogViewModel needs them and the View, not the ViewModel, owns dialog
    // construction. Exposed as internal properties on CashierViewModel for exactly this.
    private static LocalDatabase GetDb(CashierViewModel vm) => vm.Db;
    private static SyncEngine GetSync(CashierViewModel vm) => vm.Sync;
    private static string GetDeviceToken(CashierViewModel vm) => vm.DeviceToken;
}
