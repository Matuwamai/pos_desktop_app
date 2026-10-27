using Pos.Core.Interfaces;

namespace Pos.Hardware.Transports;

/// <summary>Captures every write instead of sending it anywhere — exists purely so
/// <c>EscPosEncoder</c>'s output can be asserted against exact golden bytes in tests,
/// per the original plan's note that a memory/file transport exists for that reason.</summary>
public sealed class MemoryPrinterTransport : IPrinterTransport
{
    public List<byte[]> Writes { get; } = [];

    public byte[]? LastWrite => Writes.Count > 0 ? Writes[^1] : null;

    public Task WriteAsync(byte[] data, CancellationToken ct = default)
    {
        Writes.Add(data);
        return Task.CompletedTask;
    }
}
