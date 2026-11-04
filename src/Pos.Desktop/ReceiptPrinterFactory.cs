using Pos.Core.Interfaces;
using Pos.Hardware;
using Pos.Hardware.EscPos;
using Pos.Hardware.Transports;
using Pos.Infra.Printing;

namespace Pos.Desktop;

public static class ReceiptPrinterFactory
{
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
