namespace Pos.Hardware.EscPos;

public enum TextAlign : byte { Left = 0, Center = 1, Right = 2 }
internal static class EscPosCommands
{
    public static readonly byte[] Initialize = [0x1B, 0x40]; // ESC @

    public static byte[] Align(TextAlign align) => [0x1B, 0x61, (byte)align]; // ESC a n

    public static byte[] Bold(bool on) => [0x1B, 0x45, (byte)(on ? 1 : 0)]; // ESC E n

    public static readonly byte[] LineFeed = [0x0A];
    public static readonly byte[] FullCut = [0x1D, 0x56, 0x00]; // GS V 0
    public static byte[] DrawerKick(int pin = 0, byte onTime = 25, byte offTime = 250) =>
        [0x1B, 0x70, (byte)pin, onTime, offTime]; // ESC p m t1 t2
    public static byte[] RasterImageHeader(int widthBytes, int heightDots) =>
    [
        0x1D, 0x76, 0x30, 0x00,
        (byte)(widthBytes & 0xFF), (byte)((widthBytes >> 8) & 0xFF),
        (byte)(heightDots & 0xFF), (byte)((heightDots >> 8) & 0xFF),
    ];
}
