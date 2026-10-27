using SkiaSharp;

namespace Pos.Hardware.EscPos;

/// <summary>
/// Decodes whatever image format the tenant uploaded (PNG/JPEG — see
/// <c>ReceiptDocument.LogoImageBytes</c>'s doc comment on why that's raw bytes, not a
/// pre-converted bitmap) and converts it to an ESC/POS <c>GS v 0</c> raster image: resized
/// to fit the printer's dot width, grayscale, then a plain fixed-threshold 1-bit
/// conversion — not dithered. A dithered conversion (Floyd–Steinberg) would render a
/// photo-like logo more faithfully; a plain threshold is enough for the flat-color/text
/// logos a tenant actually uploads, and keeps this simple. Revisit if a real print ever
/// looks poor.
/// </summary>
public static class LogoRasterConverter
{
    /// <param name="maxWidthDots">384 is the standard print width for an 80mm printer at
    /// 203dpi (the near-universal thermal printer resolution) — the default.</param>
    /// <returns>The complete <c>GS v 0</c> command (header + packed pixel data), ready to
    /// embed directly in the receipt byte stream — or null if the image couldn't be
    /// decoded (corrupt file, unsupported format), in which case the caller just skips
    /// printing a logo rather than failing the whole receipt.</returns>
    public static byte[]? Convert(byte[] imageBytes, int maxWidthDots = 384, byte threshold = 128)
    {
        using var original = TryDecode(imageBytes);
        if (original is null || original.Width == 0 || original.Height == 0) return null;

        var scale = Math.Min(1.0, (double)maxWidthDots / original.Width);
        var scaledWidth = Math.Max(1, (int)Math.Round(original.Width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(original.Height * scale));

        // ESC/POS raster rows are byte-aligned (8 pixels per byte): round the target
        // width up to a multiple of 8 so every row packs cleanly, with no partial byte —
        // simpler than special-casing the last bits of each row during packing.
        var widthBytes = (scaledWidth + 7) / 8;
        var packedWidth = widthBytes * 8;

        var info = new SKImageInfo(packedWidth, scaledHeight, SKColorType.Gray8, SKAlphaType.Opaque);
        using var resized = original.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        if (resized is null) return null;

        var data = new byte[widthBytes * scaledHeight];
        for (var y = 0; y < scaledHeight; y++)
        {
            for (var x = 0; x < scaledWidth; x++) // the padding columns beyond scaledWidth stay unset (white)
            {
                var gray = resized.GetPixel(x, y).Red; // Gray8: R=G=B, any channel is the gray level
                if (gray < threshold) // dark pixel -> bit set -> prints black
                {
                    var byteIndex = y * widthBytes + (x / 8);
                    var bitIndex = 7 - (x % 8); // MSB first = leftmost pixel
                    data[byteIndex] |= (byte)(1 << bitIndex);
                }
            }
        }

        var header = EscPosCommands.RasterImageHeader(widthBytes, scaledHeight);
        var result = new byte[header.Length + data.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(data, 0, result, header.Length, data.Length);
        return result;
    }

    // SKBitmap.Decode throws (ArgumentNullException, from a null internal codec —
    // confirmed, not just a documented possibility) on bytes it can't recognize as any
    // supported format. A corrupt or unsupported cached logo file must not take down
    // receipt printing with it.
    private static SKBitmap? TryDecode(byte[] imageBytes)
    {
        try
        {
            return SKBitmap.Decode(imageBytes);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
