using Pos.Core.Interfaces;

namespace Pos.Hardware.Transports;

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
