using Pos.Core.Interfaces;

namespace Pos.Hardware.Transports;

public sealed class FilePrinterTransport(string path) : IPrinterTransport
{
    public async Task WriteAsync(byte[] data, CancellationToken ct = default) =>
        await File.WriteAllBytesAsync(path, data, ct);
}
