using System.Text;
using Pos.Core.Interfaces;
using Pos.Hardware;
using Pos.Hardware.EscPos;
using Pos.Hardware.Transports;

namespace Pos.Tests;

/// <summary>
/// Golden-byte assertions against <see cref="EscPosEncoder"/>, via
/// <see cref="MemoryPrinterTransport"/> — exactly what the original plan meant by "memory/
/// file transport enables golden-byte tests."
/// </summary>
public class EscPosEncoderTests
{
    private static ReceiptDocument SampleReceipt(string tenantName = "Acme Coffee", string? customerName = null) => new(
        TenantName: tenantName, LogoImageBytes: null, LocationName: "Main Street",
        TerminalReceiptNo: "T1-20260101-0001", IssuedAt: new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
        CashierName: "Jane", CustomerName: customerName,
        Lines: [new ReceiptLine("Latte", "+ Extra shot", 2, 10.49m, 20.98m)],
        Subtotal: 20.98m, DiscountTotal: 0, TaxTotal: 3.36m, Total: 24.34m,
        Payments: [new ReceiptPayment("CASH", 25.00m)]);

    [Fact]
    public void EncodeDrawerKick_is_the_exact_ESC_p_sequence()
    {
        var bytes = EscPosEncoder.EncodeDrawerKick();
        Assert.Equal([0x1B, 0x70, 0x00, 25, 250], bytes);
    }

    [Fact]
    public void EncodeReceipt_starts_with_initialize_and_ends_with_a_full_cut()
    {
        var bytes = EscPosEncoder.EncodeReceipt(SampleReceipt(), new PrinterProfile(), logoRaster: null);

        Assert.Equal<byte>([0x1B, 0x40], bytes[..2]); // ESC @
        Assert.Equal<byte>([0x1D, 0x56, 0x00], bytes[^3..]); // GS V 0
    }

    [Fact]
    public void EncodeReceipt_embeds_the_logo_raster_bytes_verbatim_right_after_initialize()
    {
        var logo = new byte[] { 0x1D, 0x76, 0x30, 0x00, 1, 0, 1, 0, 0xFF }; // a fake but well-formed-looking raster command
        var bytes = EscPosEncoder.EncodeReceipt(SampleReceipt(), new PrinterProfile(), logoRaster: logo);

        // Initialize, then "align center", then the logo bytes verbatim.
        var afterInit = bytes[2..];
        var afterAlign = afterInit[3..]; // ESC a n is 3 bytes
        Assert.Equal<byte>(logo, afterAlign[..logo.Length]);
    }

    [Fact]
    public void EncodeReceipt_contains_the_tenant_name_as_plain_ascii_text()
    {
        var bytes = EscPosEncoder.EncodeReceipt(SampleReceipt(), new PrinterProfile(), logoRaster: null);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.Contains("Acme Coffee", text);
        Assert.Contains("T1-20260101-0001", text);
        Assert.Contains("Served by Jane", text);
    }

    [Fact]
    public void EncodeReceipt_sanitizes_non_ascii_characters_to_question_marks()
    {
        var bytes = EscPosEncoder.EncodeReceipt(SampleReceipt(tenantName: "Café Münster"), new PrinterProfile(), logoRaster: null);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.Contains("Caf? M?nster", text); // not garbled multi-byte UTF-8, and not silently dropped
    }

    [Fact]
    public void EncodeReceipt_includes_customer_name_only_when_present()
    {
        var withCustomer = Encoding.ASCII.GetString(EscPosEncoder.EncodeReceipt(SampleReceipt(customerName: "Alex"), new PrinterProfile(), null));
        var withoutCustomer = Encoding.ASCII.GetString(EscPosEncoder.EncodeReceipt(SampleReceipt(), new PrinterProfile(), null));

        Assert.Contains("Customer: Alex", withCustomer);
        Assert.DoesNotContain("Customer:", withoutCustomer);
    }

    [Fact]
    public void Item_and_total_lines_right_align_the_amount_to_the_configured_width()
    {
        var profile = new PrinterProfile(CharsPerLine: 32);
        var bytes = EscPosEncoder.EncodeReceipt(SampleReceipt(), profile, logoRaster: null);
        var lines = Encoding.ASCII.GetString(bytes).Split('\n');

        // "Subtotal" (unlike "TOTAL") isn't preceded by a Bold() escape sequence on the
        // same line, so splitting on '\n' alone gives a clean, directly-comparable
        // string — exactly CharsPerLine printable columns, nothing invisible mixed in.
        var subtotalLine = Assert.Single(lines, l => l.StartsWith("Subtotal", StringComparison.Ordinal));
        Assert.Equal(32, subtotalLine.Length);
        Assert.EndsWith("20.98", subtotalLine);
    }

    [Fact]
    public async Task EscPosReceiptPrinter_PrintAsync_writes_exactly_one_job_matching_the_encoder()
    {
        var transport = new MemoryPrinterTransport();
        var printer = new EscPosReceiptPrinter(transport, new PrinterProfile());

        await printer.PrintAsync(SampleReceipt());

        Assert.Single(transport.Writes);
        Assert.Equal(EscPosEncoder.EncodeReceipt(SampleReceipt(), new PrinterProfile(), null), transport.LastWrite);
    }

    [Fact]
    public async Task EscPosReceiptPrinter_OpenCashDrawerAsync_writes_only_the_drawer_kick()
    {
        var transport = new MemoryPrinterTransport();
        var printer = new EscPosReceiptPrinter(transport, new PrinterProfile());

        await printer.OpenCashDrawerAsync();

        Assert.Equal([EscPosEncoder.EncodeDrawerKick()], transport.Writes);
    }
}
