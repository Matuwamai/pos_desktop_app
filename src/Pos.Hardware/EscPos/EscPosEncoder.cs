using System.Globalization;
using System.Text;
using Pos.Core.Interfaces;
using Pos.Core.Pricing;

namespace Pos.Hardware.EscPos;

/// <summary>
/// Builds the complete ESC/POS byte stream for one receipt — hand-written, not a
/// third-party library, per the plan: small and directly testable against
/// <c>MemoryPrinterTransport</c>/<c>FilePrinterTransport</c> with exact golden-byte
/// assertions (see <c>EscPosEncoderTests</c>).
/// </summary>
public static class EscPosEncoder
{
    public static byte[] EncodeReceipt(ReceiptDocument receipt, PrinterProfile profile, byte[]? logoRaster)
    {
        using var ms = new MemoryStream();

        void Write(byte[] bytes) => ms.Write(bytes, 0, bytes.Length);
        void WriteLine(string text = "")
        {
            Write(Encoding.ASCII.GetBytes(Sanitize(text)));
            Write(EscPosCommands.LineFeed);
        }

        var width = profile.CharsPerLine;
        var rule = new string('-', width);
        // "0.00"/"0" depending on the receipt's own currency — JPY-style 0-decimal
        // currencies print whole units, matching the server's receipt.js convention.
        var decimalDigits = CurrencyInfo.GetDecimalDigits(receipt.Currency);
        var moneyFormat = decimalDigits == 0 ? "0" : "0." + new string('0', decimalDigits);

        Write(EscPosCommands.Initialize);

        if (logoRaster is not null)
        {
            Write(EscPosCommands.Align(TextAlign.Center));
            Write(logoRaster);
            Write(EscPosCommands.LineFeed);
        }

        Write(EscPosCommands.Align(TextAlign.Center));
        Write(EscPosCommands.Bold(true));
        WriteLine(receipt.TenantName);
        Write(EscPosCommands.Bold(false));
        WriteLine(receipt.LocationName);
        WriteLine(receipt.IssuedAt.ToLocalTime().ToString("g", CultureInfo.InvariantCulture));

        Write(EscPosCommands.Align(TextAlign.Left));
        WriteLine(rule);
        // A plain hyphen, not a middle dot — Sanitize() below maps anything above
        // ASCII 127 to '?', which would otherwise mangle a nicer separator.
        WriteLine($"Receipt {receipt.TerminalReceiptNo} - {receipt.Currency}");
        WriteLine($"Served by {receipt.CashierName}");
        if (receipt.CustomerName is { Length: > 0 }) WriteLine($"Customer: {receipt.CustomerName}");
        WriteLine(rule);

        foreach (var line in receipt.Lines)
        {
            WriteLine(Truncate(line.Description, width));
            if (line.ModifiersSummary is { Length: > 0 }) WriteLine(Truncate("  + " + line.ModifiersSummary, width));
            var left = $"  {line.Quantity.ToString("0.##", CultureInfo.InvariantCulture)} x {line.UnitPrice.ToString(moneyFormat, CultureInfo.InvariantCulture)}";
            WriteLine(Columns(left, line.LineTotal.ToString(moneyFormat, CultureInfo.InvariantCulture), width));
        }

        WriteLine(rule);
        WriteLine(Columns("Subtotal", receipt.Subtotal.ToString(moneyFormat, CultureInfo.InvariantCulture), width));
        if (receipt.DiscountTotal > 0)
            WriteLine(Columns("Discount", "-" + receipt.DiscountTotal.ToString(moneyFormat, CultureInfo.InvariantCulture), width));
        WriteLine(Columns("Tax", receipt.TaxTotal.ToString(moneyFormat, CultureInfo.InvariantCulture), width));
        Write(EscPosCommands.Bold(true));
        WriteLine(Columns("TOTAL", receipt.Total.ToString(moneyFormat, CultureInfo.InvariantCulture), width));
        Write(EscPosCommands.Bold(false));
        WriteLine(rule);

        foreach (var payment in receipt.Payments)
            WriteLine(Columns(payment.Method, payment.Amount.ToString(moneyFormat, CultureInfo.InvariantCulture), width));

        WriteLine(rule);
        Write(EscPosCommands.Align(TextAlign.Center));
        WriteLine("Thank you for your business!");
        WriteLine();
        WriteLine();

        Write(EscPosCommands.FullCut);
        return ms.ToArray();
    }

    public static byte[] EncodeDrawerKick() => EscPosCommands.DrawerKick();

    /// <summary>Left-aligned label, right-aligned value, padded to fill exactly
    /// <paramref name="width"/> columns (at least one space between them even if the
    /// combined text would otherwise overflow).</summary>
    private static string Columns(string left, string right, int width)
    {
        var padding = Math.Max(1, width - left.Length - right.Length);
        return left + new string(' ', padding) + right;
    }

    private static string Truncate(string text, int width) => text.Length <= width ? text : text[..width];

    // Thermal printers vary in code page support; ASCII is the one encoding every
    // ESC/POS printer is guaranteed to render correctly without a code-page switch
    // command first. Non-ASCII characters (accented names, etc.) degrade to '?' rather
    // than risk garbled output on a printer configured for a different page.
    private static string Sanitize(string text)
    {
        Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
        for (var i = 0; i < text.Length; i++) buffer[i] = text[i] <= 127 ? text[i] : '?';
        return new string(buffer);
    }
}
