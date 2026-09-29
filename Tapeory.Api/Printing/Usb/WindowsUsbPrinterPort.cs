using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tapeory.Api.Printing.Usb;

/// <summary>
/// Printers installed in Windows (Settings → Printers &amp; scanners): the list is Windows' own, and
/// a job goes through the print spooler as RAW data, so the Brother raster reaches the printer
/// unchanged whatever driver is installed for it (Brother's, or "Generic / Text Only").
/// </summary>
public sealed class WindowsUsbPrinterPort : IUsbPrinterPort
{
    public IReadOnlyList<UsbPrinterInfo> List()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        return [.. InstalledPrinters().Order(StringComparer.OrdinalIgnoreCase).Select(name => new UsbPrinterInfo(name, name, ModelIn(name)))];
    }

    public Task<RawSendResult> SendAsync(string identifier, byte[] data, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(RawSendResult.Failure("Windows printers are only available on Windows."));
        }

        return Task.Run(() => Send(identifier, data), cancellationToken);
    }

    /// <summary>"Brother PT-P750W" → "PT-P750W": the model part of a printer's name, if any.</summary>
    public static string? ModelIn(string name) =>
        name.Split([' ', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(word => word.StartsWith("PT-", StringComparison.OrdinalIgnoreCase)
                || word.StartsWith("QL-", StringComparison.OrdinalIgnoreCase));

    [SupportedOSPlatform("windows")]
    private static RawSendResult Send(string printerName, byte[] data)
    {
        if (!OpenPrinter(printerName, out var printer, IntPtr.Zero))
        {
            return RawSendResult.Failure($"Windows doesn't know a printer called \"{printerName}\". Is it still installed?");
        }

        try
        {
            var document = new DocInfo { Name = "Tapeory label", DataType = "RAW" };

            if (StartDocPrinter(printer, 1, ref document) == 0)
            {
                return Failed("starting the print job");
            }

            try
            {
                if (!StartPagePrinter(printer))
                {
                    return Failed("starting the page");
                }

                var handle = GCHandle.Alloc(data, GCHandleType.Pinned);

                try
                {
                    if (!WritePrinter(printer, handle.AddrOfPinnedObject(), data.Length, out var written) || written != data.Length)
                    {
                        return Failed("sending the label data");
                    }
                }
                finally
                {
                    handle.Free();
                }

                EndPagePrinter(printer);
            }
            finally
            {
                EndDocPrinter(printer);
            }

            return RawSendResult.Success();
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    private static RawSendResult Failed(string step) =>
        RawSendResult.Failure($"Windows refused {step}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

    [SupportedOSPlatform("windows")]
    private static List<string> InstalledPrinters()
    {
        const int local = 0x2, connections = 0x4;
        EnumPrinters(local | connections, null, 4, IntPtr.Zero, 0, out var needed, out _);

        if (needed == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(needed);

        try
        {
            if (!EnumPrinters(local | connections, null, 4, buffer, needed, out _, out var count))
            {
                return [];
            }

            var size = Marshal.SizeOf<PrinterInfo4>();
            return [.. Enumerable.Range(0, count)
                .Select(index => Marshal.PtrToStructure<PrinterInfo4>(buffer + index * size).PrinterName)
                .OfType<string>()];
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo4
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? PrinterName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ServerName;
        public int Attributes;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr printer, int level, ref DocInfo document);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr printer, IntPtr data, int count, out int written);

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool EnumPrinters(int flags, string? name, int level, IntPtr buffer, int size, out int needed, out int returned);
}
