using System.Net.Sockets;
using Pos.Core.Interfaces;

namespace Pos.Hardware.Transports;

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
