using Pos.Hardware.EscPos;
using SkiaSharp;

namespace Pos.Tests;

public class LogoRasterConverterTests
{
    private static byte[] MakeSolidPng(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void A_solid_black_image_packs_to_all_set_bits()
    {
        var png = MakeSolidPng(8, 8, SKColors.Black);
        var raster = LogoRasterConverter.Convert(png, maxWidthDots: 8);

        Assert.NotNull(raster);
        var header = raster!.AsSpan(0, 8);
        Assert.Equal([0x1D, 0x76, 0x30, 0x00, 1, 0, 8, 0], header.ToArray()); // widthBytes=1, height=8
        var pixelData = raster.AsSpan(8).ToArray();
        Assert.All(pixelData, b => Assert.Equal(0xFF, b)); // every pixel dark -> every bit set
    }

    [Fact]
    public void A_solid_white_image_packs_to_all_clear_bits()
    {
        var png = MakeSolidPng(8, 8, SKColors.White);
        var raster = LogoRasterConverter.Convert(png, maxWidthDots: 8);

        Assert.NotNull(raster);
        var pixelData = raster!.AsSpan(8).ToArray();
        Assert.All(pixelData, b => Assert.Equal(0x00, b));
    }

    [Fact]
    public void Width_is_rounded_up_to_a_whole_byte()
    {
        var png = MakeSolidPng(5, 3, SKColors.Black); // not a multiple of 8
        var raster = LogoRasterConverter.Convert(png, maxWidthDots: 100);

        Assert.NotNull(raster);
        var widthBytes = raster![4] | (raster[5] << 8);
        Assert.Equal(1, widthBytes); // ceil(5/8) = 1, never a fractional byte
    }

    [Fact]
    public void Wider_than_maxWidthDots_is_scaled_down_to_fit()
    {
        var png = MakeSolidPng(800, 400, SKColors.Black);
        var raster = LogoRasterConverter.Convert(png, maxWidthDots: 384);

        Assert.NotNull(raster);
        var widthBytes = raster![4] | (raster[5] << 8);
        Assert.True(widthBytes * 8 <= 384 + 7); // packed width never exceeds the requested bound (plus byte-rounding slack)
    }

    [Fact]
    public void Garbage_bytes_return_null_instead_of_throwing()
    {
        var result = LogoRasterConverter.Convert([1, 2, 3, 4, 5]);
        Assert.Null(result);
    }
}
