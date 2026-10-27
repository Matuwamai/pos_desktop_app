using System.IO.Ports;
using Pos.Core.Interfaces;

namespace Pos.Hardware.Transports;

/// <summary>A printer wired over RS-232 (or a USB-to-serial adapter presenting as a COM/
/// tty port). 9600 baud, 8N1 is the standard default most serial ESC/POS printers ship
/// configured for. Opens the port fresh per job, same reasoning as
/// <see cref="NetworkPrinterTransport"/>.</summary>
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
