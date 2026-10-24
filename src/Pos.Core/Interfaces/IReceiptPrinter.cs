namespace Pos.Core.Interfaces;

/// <summary>
/// What to print — built once per sale and handed to whichever
/// <see cref="IReceiptPrinter"/> transport is configured for the terminal (Phase 3:
/// Network/Serial/USB/System; a logo is rasterised to an ESC/POS bitmap there).
/// Kept deliberately minimal for Phase 1 — this is the seam the hardware layer
/// will hang off, not a finished receipt layout.
/// </summary>
public sealed record ReceiptDocument(
    string TenantName,
    string LocationName,
    string TerminalReceiptNo,
    DateTimeOffset IssuedAt,
    IReadOnlyList<ReceiptLine> Lines,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal Total);

public sealed record ReceiptLine(string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>
/// How bytes travel to a physical printer — deliberately separate from
/// <see cref="IReceiptPrinter"/> (what to print), so Network/Serial/USB/System
/// transports (Phase 3) can share one ESC/POS encoder.
/// </summary>
public interface IReceiptPrinter
{
    Task PrintAsync(ReceiptDocument receipt, CancellationToken ct = default);
    Task OpenCashDrawerAsync(CancellationToken ct = default);
}
