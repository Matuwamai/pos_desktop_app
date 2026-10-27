using System.Net.Sockets;
using Pos.Core.Interfaces;

namespace Pos.Hardware.Transports;

/// <summary>TCP to the printer's raw ESC/POS listener — port 9100 is the near-universal
/// default ("JetDirect"/"RAW" port) every network thermal printer answers on. Opens a
/// fresh connection per job rather than keeping one alive: simplest, and robust against
/// a printer that was power-cycled since the last print.</summary>
public sealed class NetworkPrinterTransport(string host, int port = 9100) : IPrinterTransport
{
    public async Task WriteAsync(byte[] data, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct);
        await using var stream = client.GetStream();
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
    }
}
