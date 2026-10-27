using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Interfaces;
using Pos.Infra.Printing;
using Pos.Infra.Storage;

namespace Pos.Desktop.ViewModels;

/// <summary>
/// Configures which printer transport this terminal uses — the feature is unusable
/// without somewhere to set this. Text fields rather than typed inputs throughout
/// (host, port, baud rate, …) to keep the form simple; validated/defaulted on save in
/// <see cref="BuildSettings"/>, same pattern as the other dialogs' amount fields.
/// </summary>
public sealed partial class PrinterSettingsDialogViewModel : ViewModelBase
{
    private readonly LocalDatabase _db;

    public PrinterSettingsDialogViewModel(LocalDatabase db, PrinterSettings current)
    {
        _db = db;
        Transport = current.Transport;
        NetworkHost = current.NetworkHost ?? "";
        NetworkPort = (current.NetworkPort ?? 9100).ToString(CultureInfo.InvariantCulture);
        SerialPortName = current.SerialPortName ?? "";
        SerialBaudRate = (current.SerialBaudRate ?? 9600).ToString(CultureInfo.InvariantCulture);
        SystemPrinterName = current.SystemPrinterName ?? "";
        CharsPerLine = current.CharsPerLine.ToString(CultureInfo.InvariantCulture);
        OpenDrawerOnCash = current.OpenDrawerOnCash;
    }

    public static string[] TransportOptions { get; } =
    [
        PrinterTransportKind.None, PrinterTransportKind.Network, PrinterTransportKind.Serial, PrinterTransportKind.System,
    ];

    [ObservableProperty] private string _transport;
    [ObservableProperty] private string _networkHost;
    [ObservableProperty] private string _networkPort;
    [ObservableProperty] private string _serialPortName;
    [ObservableProperty] private string _serialBaudRate;
    [ObservableProperty] private string _systemPrinterName;
    [ObservableProperty] private string _charsPerLine;
    [ObservableProperty] private bool _openDrawerOnCash;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isBusy;

    public PrinterSettings? Result { get; private set; }
    public Action<bool>? RequestClose { get; set; }

    private PrinterSettings BuildSettings() => new(
        Transport,
        string.IsNullOrWhiteSpace(NetworkHost) ? null : NetworkHost.Trim(),
        int.TryParse(NetworkPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : null,
        string.IsNullOrWhiteSpace(SerialPortName) ? null : SerialPortName.Trim(),
        int.TryParse(SerialBaudRate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var baud) ? baud : null,
        string.IsNullOrWhiteSpace(SystemPrinterName) ? null : SystemPrinterName.Trim(),
        int.TryParse(CharsPerLine, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chars) && chars > 0 ? chars : 48,
        OpenDrawerOnCash);

    [RelayCommand]
    private async Task TestPrintAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var printer = ReceiptPrinterFactory.Build(BuildSettings());
            if (printer is null)
            {
                StatusMessage = "Choose a transport and fill in its details first.";
                return;
            }

            var sample = new ReceiptDocument(
                TenantName: "Test receipt", LogoImageBytes: null, LocationName: "Printer settings",
                // "Test Cashier", not an em-dash placeholder — EscPosEncoder sanitizes
                // non-ASCII to '?' (deliberately, see its doc comment), which made this
                // sample receipt confusingly print "Served by ?" until this fix.
                TerminalReceiptNo: "T-TEST-0001", IssuedAt: DateTimeOffset.Now, CashierName: "Test Cashier", CustomerName: null,
                Lines: [new ReceiptLine("Sample item", "+ Sample modifier", 1, 1.00m, 1.00m)],
                Subtotal: 1.00m, DiscountTotal: 0, TaxTotal: 0, Total: 1.00m,
                Payments: [new ReceiptPayment("CASH", 1.00m)]);
            await printer.PrintAsync(sample);
            StatusMessage = "Test receipt sent.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't print: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Result = BuildSettings();
        await PrinterSettingsStore.SaveAsync(_db, Result);
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
