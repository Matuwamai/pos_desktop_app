using Pos.Core.Interfaces;

namespace Pos.Hardware.Transports;

/// <summary>Writes each print job to a file instead of a real printer — a dev/debug
/// fallback (inspect the exact bytes with a hex dump) and the other half of what the
/// plan calls "memory/file transport enables golden-byte tests." Each job overwrites
/// the file; <paramref name="path"/> is the single most recent job, not a log.</summary>
public sealed class FilePrinterTransport(string path) : IPrinterTransport
{
    public async Task WriteAsync(byte[] data, CancellationToken ct = default) =>
        await File.WriteAllBytesAsync(path, data, ct);
}
