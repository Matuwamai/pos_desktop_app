namespace Pos.Core.Interfaces;

/// <summary>
/// What to print — built once per sale and handed to whichever
/// <see cref="IReceiptPrinter"/> is configured for the terminal. Deliberately carries a
/// raw <see cref="LogoImageBytes"/> (whatever was cached — PNG/JPEG, not pre-converted)
/// rather than an already-rasterised bitmap: keeps Pos.Core free of any image-library
/// dependency, since only Pos.Hardware's printer implementation needs one
/// (<c>LogoRasterConverter</c>, SkiaSharp).
/// </summary>
public sealed record ReceiptDocument(
    string TenantName,
    byte[]? LogoImageBytes,
    string LocationName,
    string TerminalReceiptNo,
    DateTimeOffset IssuedAt,
    string CashierName,
    string? CustomerName,
    IReadOnlyList<ReceiptLine> Lines,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal Total,
    IReadOnlyList<ReceiptPayment> Payments);

public sealed record ReceiptLine(string Description, string? ModifiersSummary, decimal Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record ReceiptPayment(string Method, decimal Amount);

/// <summary>
/// How bytes travel to a physical printer — deliberately separate from
/// <see cref="IReceiptPrinter"/> (what to print), so Network/Serial/System/Memory/File
/// transports (Pos.Hardware) can share one ESC/POS encoder. Each call is a complete
/// print job: implementations open their connection, write, and close — simplest and
/// most robust against a printer that was power-cycled or disconnected since the last job.
/// </summary>
public interface IPrinterTransport
{
    Task WriteAsync(byte[] data, CancellationToken ct = default);
}

/// <summary>
/// Implemented by <c>Pos.Hardware.EscPosReceiptPrinter</c> against whichever
/// <see cref="IPrinterTransport"/> is configured. <c>null</c>/unconfigured is a valid
/// terminal state — printing is optional, the on-screen receipt is the reliable fallback.
/// </summary>
public interface IReceiptPrinter
{
    Task PrintAsync(ReceiptDocument receipt, CancellationToken ct = default);
    Task OpenCashDrawerAsync(CancellationToken ct = default);
}
