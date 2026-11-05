using System.IO.Ports;
using Pos.Core.Interfaces;

namespace Pos.Hardware.Transports;

public sealed class SerialPrinterTransport(string portName, int baudRate = 9600) : IPrinterTransport
{
    public async Task WriteAsync(byte[] data, CancellationToken ct = default)
    {
        using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            WriteTimeout = 5000,
        };
        port.Open();
        try
        {
            await port.BaseStream.WriteAsync(data, ct);
            await port.BaseStream.FlushAsync(ct);
        }
        finally
        {
            port.Close();
        }
    }
}
