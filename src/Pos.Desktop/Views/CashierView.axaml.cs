using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Pos.Core.Domain;
using Pos.Core.Interfaces;
using Pos.Core.Sync;
using Pos.Desktop.ViewModels;
using Pos.Infra.Api;
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

        vm.ShowPaymentDialog = async (total, mpesaType, mpesaShortcode, mpesaStkConfigured) =>
        {
            var dialogVm = new PaymentDialogViewModel(total, mpesaType, mpesaShortcode, mpesaStkConfigured, GetApi(vm), vm.DeviceToken, vm.IsOnline);
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

        vm.ShowPrinterSettingsDialog = async current =>
        {
            var dialogVm = new PrinterSettingsDialogViewModel(GetDb(vm), current);
            var dialog = new PrinterSettingsDialog(dialogVm);
            var saved = await dialog.ShowDialog<bool>(Owner!);
            return saved ? dialogVm.Result : null;
        };

        vm.ShowRefundDialog = async () =>
        {
            var dialogVm = new RefundLookupDialogViewModel(
                GetApi(vm), vm.DeviceToken, vm.LocationId, vm.SignedInAs.Id,
                vm.SignedInAs.Permissions.Contains("orders.refund"), vm.IsOnline);
            var dialog = new RefundLookupDialog(dialogVm);
            return await dialog.ShowDialog<bool>(Owner!);
        };

        vm.OnDeviceInvalid = ex => (Owner as MainWindow)?.AppViewModel.HandleDeviceRevokedAsync(ex) ?? Task.CompletedTask;
    }

    private void OnOpenLogsFolderClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Pos.Infra.AppPaths.LogsDirectory);
        OpenFolder(Pos.Infra.AppPaths.LogsDirectory);
    }

    private void OnRestartToUpdateClick(object? sender, RoutedEventArgs e) =>
        Pos.Infra.Updates.UpdateChecker.ApplyAndRestart();

    // Same per-OS branch shape Pos.Hardware's SystemPrinterTransport (Phase 3) already
    // uses for OperatingSystem.IsWindows()-gated behavior, applied here to open a folder
    // instead of spooling to a printer.
    private static void OpenFolder(string path)
    {
        if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", path);
        else if (OperatingSystem.IsMacOS()) Process.Start("open", path);
        else Process.Start("xdg-open", path);
    }

    // CashierViewModel's db/sync/deviceToken fields are private — these small reflection-free
    // accessors would normally just be constructor parameters passed straight through, but
    // CustomerDialogViewModel needs them and the View, not the ViewModel, owns dialog
    // construction. Exposed as internal properties on CashierViewModel for exactly this.
    private static LocalDatabase GetDb(CashierViewModel vm) => vm.Db;
    private static SyncEngine GetSync(CashierViewModel vm) => vm.Sync;
    private static ApiClient GetApi(CashierViewModel vm) => vm.Api;
    private static string GetDeviceToken(CashierViewModel vm) => vm.DeviceToken;
}
