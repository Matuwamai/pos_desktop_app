using System.Globalization;
using Pos.Core.Pricing;

namespace Pos.Core.Interfaces;

public sealed record ReceiptDocument(
    string TenantName,
    byte[]? LogoImageBytes,
    string Currency,
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
    IReadOnlyList<ReceiptPayment> Payments,
    decimal CashRoundingAdjustment = 0m,
    // null means "use the hardcoded fallback" — see EscPosEncoder.cs. The three show
    // flags default true so every existing call site (tests, PrinterSettingsDialog's
    // test print) keeps behaving exactly as before without needing to pass them.
    string? FooterMessage = null,
    bool ShowCustomerName = true,
    bool ShowCashierName = true,
    bool ShowLogo = true)
{
    private string MoneyFormat => CurrencyInfo.GetDecimalDigits(Currency) == 0 ? "0" : "0." + new string('0', CurrencyInfo.GetDecimalDigits(Currency));

    public string SubtotalText => Subtotal.ToString(MoneyFormat, CultureInfo.InvariantCulture);
    public string DiscountTotalText => "-" + DiscountTotal.ToString(MoneyFormat, CultureInfo.InvariantCulture);
    public string TaxTotalText => TaxTotal.ToString(MoneyFormat, CultureInfo.InvariantCulture);
    public bool HasCashRoundingAdjustment => CashRoundingAdjustment != 0m;
    public string CashRoundingAdjustmentText =>
        (CashRoundingAdjustment > 0 ? "" : "-") + Math.Abs(CashRoundingAdjustment).ToString(MoneyFormat, CultureInfo.InvariantCulture);
    public string TotalText => Total.ToString(MoneyFormat, CultureInfo.InvariantCulture);
    // Convenience for XAML bindings, which can't express a compound boolean or a
    // string-with-fallback inline the way EscPosEncoder.cs's plain C# code can.
    public bool ShowCustomerNameLine => ShowCustomerName && CustomerName is { Length: > 0 };
    public string FooterMessageText => FooterMessage is { Length: > 0 } ? FooterMessage : "Thank you for your business!";
}

/// <summary><see cref="DecimalDigits"/> defaults to 2 and is set at construction time
/// from the receipt's own <see cref="ReceiptDocument.Currency"/> — a plain record has no
/// ambient state to pull it from otherwise.</summary>
public sealed record ReceiptLine(string Description, string? ModifiersSummary, decimal Quantity, decimal UnitPrice, decimal LineTotal, int DecimalDigits = 2)
{
    private string MoneyFormat => DecimalDigits == 0 ? "0" : "0." + new string('0', DecimalDigits);
    public string UnitPriceText => UnitPrice.ToString(MoneyFormat, CultureInfo.InvariantCulture);
    public string LineTotalText => LineTotal.ToString(MoneyFormat, CultureInfo.InvariantCulture);
}

public sealed record ReceiptPayment(string Method, decimal Amount, int DecimalDigits = 2)
{
    public string AmountText => Amount.ToString(DecimalDigits == 0 ? "0" : "0." + new string('0', DecimalDigits), CultureInfo.InvariantCulture);
}

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
