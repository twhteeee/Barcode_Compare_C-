using System;
using System.IO.Ports;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class ModemStatusChecker
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCommModemStatus(IntPtr hFile, out uint lpModemStat);

    private const uint MS_CTS_ON = 0x0010;
    private const uint MS_DSR_ON = 0x0020;
    private const uint MS_RING_ON = 0x0040;
    private const uint MS_RLSD_ON = 0x0080; // DCD

    public static bool AnySignalActive(SerialPort port)
    {
        try
        {
            // Reach into SerialPort's private internal stream to get its real OS file handle
            var baseStreamField = typeof(SerialPort).GetField("internalSerialStream",
                BindingFlags.NonPublic | BindingFlags.Instance);
            object stream = baseStreamField?.GetValue(port);
            if (stream == null) return false;

            var handleField = stream.GetType().GetField("_handle",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var safeHandle = handleField?.GetValue(stream) as Microsoft.Win32.SafeHandles.SafeFileHandle;
            if (safeHandle == null || safeHandle.IsInvalid) return false;

            if (GetCommModemStatus(safeHandle.DangerousGetHandle(), out uint status))
            {
                bool cts = (status & MS_CTS_ON) != 0;
                bool dsr = (status & MS_DSR_ON) != 0;
                bool ring = (status & MS_RING_ON) != 0;
                bool dcd = (status & MS_RLSD_ON) != 0;
                return cts || dsr || ring || dcd;
            }
        }
        catch
        {
            // Reflection into private framework internals can break across .NET versions —
            // if it fails, just report "unknown" rather than crashing.
        }
        return false;
    }
}