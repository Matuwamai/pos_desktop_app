using Pos.Core.Interfaces;
using Pos.Hardware;
using Pos.Hardware.EscPos;
using Pos.Hardware.Transports;
using Pos.Infra.Printing;

namespace Pos.Desktop;

/// <summary>
/// Turns saved <see cref="PrinterSettings"/> into a constructed <see cref="IReceiptPrinter"/>
/// — the one place this solution builds a concrete transport from configuration, same
/// spirit as the rest of this app's hand-wired composition (<c>AppViewModel</c>,
/// <c>CashierView</c>'s dialog hooks). Lives in Pos.Desktop, not Pos.Infra: Pos.Infra only
/// persists the settings row, it has no reference to Pos.Hardware and shouldn't need one.
/// </summary>
public static class ReceiptPrinterFactory
{
    /// <returns>null for <see cref="PrinterTransportKind.None"/> or incomplete settings —
    /// a valid, common state (printing is optional).</returns>
    public static IReceiptPrinter? Build(PrinterSettings settings)
    {
        var profile = new PrinterProfile(settings.CharsPerLine);
        IPrinterTransport? transport = settings.Transport switch
        {
            PrinterTransportKind.Network when settings.NetworkHost is { Length: > 0 } host =>
                new NetworkPrinterTransport(host, settings.NetworkPort ?? 9100),
            PrinterTransportKind.Serial when settings.SerialPortName is { Length: > 0 } port =>
                new SerialPrinterTransport(port, settings.SerialBaudRate ?? 9600),
            PrinterTransportKind.System when settings.SystemPrinterName is { Length: > 0 } name =>
                new SystemPrinterTransport(name),
            _ => null,
        };
        return transport is null ? null : new EscPosReceiptPrinter(transport, profile);
    }
}
