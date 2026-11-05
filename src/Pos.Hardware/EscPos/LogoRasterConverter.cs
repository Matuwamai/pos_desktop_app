using SkiaSharp;

namespace Pos.Hardware.EscPos;

public static class LogoRasterConverter
{
    public static byte[]? Convert(byte[] imageBytes, int maxWidthDots = 384, byte threshold = 128)
    {
        using var original = TryDecode(imageBytes);
        if (original is null || original.Width == 0 || original.Height == 0) return null;

        var scale = Math.Min(1.0, (double)maxWidthDots / original.Width);
        var scaledWidth = Math.Max(1, (int)Math.Round(original.Width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(original.Height * scale));
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
