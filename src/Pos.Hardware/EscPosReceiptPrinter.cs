using Pos.Core.Interfaces;
using Pos.Hardware.EscPos;

namespace Pos.Hardware;

/// <summary>
/// The one <see cref="IReceiptPrinter"/> implementation — ESC/POS over whichever
/// <see cref="IPrinterTransport"/> is configured. Converts the logo once per print (not
/// cached here; <see cref="ReceiptDocument.LogoImageBytes"/> is already whatever the
/// sync engine last cached, so re-converting per print is cheap enough not to bother).
/// </summary>
public sealed class EscPosReceiptPrinter(IPrinterTransport transport, PrinterProfile profile) : IReceiptPrinter
{
    public async Task PrintAsync(ReceiptDocument receipt, CancellationToken ct = default)
    {
        var logoRaster = receipt.LogoImageBytes is { Length: > 0 } logo ? LogoRasterConverter.Convert(logo) : null;
        var bytes = EscPosEncoder.EncodeReceipt(receipt, profile, logoRaster);
        await transport.WriteAsync(bytes, ct);
    }

    public async Task OpenCashDrawerAsync(CancellationToken ct = default) =>
        await transport.WriteAsync(EscPosEncoder.EncodeDrawerKick(), ct);
}
