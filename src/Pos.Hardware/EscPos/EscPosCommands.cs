namespace Pos.Hardware.EscPos;

public enum TextAlign : byte { Left = 0, Center = 1, Right = 2 }

/// <summary>
/// Raw ESC/POS byte sequences — the common subset nearly every clone/real thermal
/// printer supports. One place to look these up rather than scattering magic byte
/// arrays through <see cref="EscPosEncoder"/>.
/// </summary>
internal static class EscPosCommands
{
    public static readonly byte[] Initialize = [0x1B, 0x40]; // ESC @

    public static byte[] Align(TextAlign align) => [0x1B, 0x61, (byte)align]; // ESC a n

    public static byte[] Bold(bool on) => [0x1B, 0x45, (byte)(on ? 1 : 0)]; // ESC E n

    public static readonly byte[] LineFeed = [0x0A];

    /// <summary>Full cut. Some clones only answer to the longer <c>GS V 66 0</c> form —
    /// if a real printer doesn't cut, that's the first thing to try instead.</summary>
    public static readonly byte[] FullCut = [0x1D, 0x56, 0x00]; // GS V 0

    /// <summary>The drawer is wired to the printer's RJ11/RJ12 port on real hardware, so
    /// this travels through the same <see cref="Pos.Core.Interfaces.IPrinterTransport"/>
    /// as everything else — never a separate connection. <paramref name="pin"/> 0 or 1
    /// selects which of the two drawer-kick pins to pulse; 0 is by far the more common
    /// wiring. Timings are in units of 2ms (25 → 50ms on, 250 → 500ms off) — the
    /// standard default most printers ship with.</summary>
    public static byte[] DrawerKick(int pin = 0, byte onTime = 25, byte offTime = 250) =>
        [0x1B, 0x70, (byte)pin, onTime, offTime]; // ESC p m t1 t2

    /// <summary>Header for a <c>GS v 0</c> raster image command — the pixel data (1bpp,
    /// MSB-first per byte = leftmost pixel, row-major) follows immediately after this,
    /// with no delimiter. <paramref name="widthBytes"/> is the row width in *bytes*
    /// (pixels/8, already byte-aligned by the caller), <paramref name="heightDots"/> the
    /// row count.</summary>
    public static byte[] RasterImageHeader(int widthBytes, int heightDots) =>
    [
        0x1D, 0x76, 0x30, 0x00,
        (byte)(widthBytes & 0xFF), (byte)((widthBytes >> 8) & 0xFF),
        (byte)(heightDots & 0xFF), (byte)((heightDots >> 8) & 0xFF),
    ];
}
