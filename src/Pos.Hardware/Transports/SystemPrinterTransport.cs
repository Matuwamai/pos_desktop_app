using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pos.Core.Interfaces;

namespace Pos.Hardware.Transports;

/// <summary>
/// Hands raw bytes to the OS's own print spooler, for a printer installed as a normal
/// system printer rather than addressed directly. Branches at runtime via
/// <see cref="OperatingSystem.IsWindows"/>, not a compile-time <c>#if</c> — this is one
/// cross-platform assembly, and a P/Invoke to a Windows-only DLL simply never gets
/// *called* on another OS, which is all that matters (the method reference itself
/// resolving lazily is fine). The Windows path is code-reviewed only — there is no
/// Windows machine available to verify it against a real spooler in this environment,
/// unlike the Linux/CUPS path below, which is live-tested (`lp`/`lpr`/`lpstat` are
/// confirmed installed on the dev box this was built on).
/// </summary>
public sealed class SystemPrinterTransport(string printerName) : IPrinterTransport
{
    public Task WriteAsync(byte[] data, CancellationToken ct = default) =>
        OperatingSystem.IsWindows() ? WriteWindowsAsync(data) : WriteCupsAsync(data, ct);

    // CUPS's `-o raw` tells it to pass the bytes straight to the printer with no
    // filtering/reformatting — exactly what a hand-built ESC/POS stream needs. Piping
    // through a child process rather than talking to libcups directly keeps this
    // dependency-free and is how most non-GUI raw-print integrations on Linux work.
    private async Task WriteCupsAsync(byte[] data, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("lp", ["-d", printerName, "-o", "raw"])
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start 'lp' — is CUPS installed?");
        await process.StandardInput.BaseStream.WriteAsync(data, ct);
        process.StandardInput.Close();
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new InvalidOperationException($"'lp' exited with code {process.ExitCode}: {stderr}");
    }

    [SupportedOSPlatform("windows")]
    private Task WriteWindowsAsync(byte[] data)
    {
        if (!WinSpool.OpenPrinter(printerName, out var handle, IntPtr.Zero))
            throw new InvalidOperationException($"Could not open printer '{printerName}' (Win32 error {Marshal.GetLastWin32Error()})");
        try
        {
            var docInfo = new WinSpool.DocInfo1 { DocName = "POS Receipt", DataType = "RAW" };
            var jobId = WinSpool.StartDocPrinter(handle, 1, ref docInfo);
            if (jobId == 0) throw new InvalidOperationException($"StartDocPrinter failed (Win32 error {Marshal.GetLastWin32Error()})");
            try
            {
                WinSpool.StartPagePrinter(handle);
                var buffer = Marshal.AllocHGlobal(data.Length);
                try
                {
                    Marshal.Copy(data, 0, buffer, data.Length);
                    if (!WinSpool.WritePrinter(handle, buffer, data.Length, out _))
                        throw new InvalidOperationException($"WritePrinter failed (Win32 error {Marshal.GetLastWin32Error()})");
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
                WinSpool.EndPagePrinter(handle);
            }
            finally
            {
                WinSpool.EndDocPrinter(handle);
            }
        }
        finally
        {
            WinSpool.ClosePrinter(handle);
        }
        return Task.CompletedTask;
    }

    [SupportedOSPlatform("windows")]
    private static class WinSpool
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DocInfo1
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string DocName;
            [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
        }

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool OpenPrinter(string printerName, out IntPtr handle, IntPtr defaults);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr handle);

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int StartDocPrinter(IntPtr handle, int level, ref DocInfo1 docInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndDocPrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool StartPagePrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndPagePrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool WritePrinter(IntPtr handle, IntPtr data, int bufferSize, out int bytesWritten);
    }
}
