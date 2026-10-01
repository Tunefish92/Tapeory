using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tapeory.Api.Printing.Usb;

/// <summary>
/// USB printers on Linux: the kernel's usblp driver gives each one a device file (/dev/usb/lp0,
/// lp1, …) that takes raw printer data, and publishes the IEEE 1284 device id it reports
/// ("MFG:Brother;MDL:PT-P750W;…") under /sys/class/usbmisc/lpN/device/ieee1284_id.
/// Writing needs the user in the "lp" group (the device's group on most distributions).
/// </summary>
public sealed class LinuxUsbPrinterPort(string deviceFolder = "/dev/usb", string sysFolder = "/sys/class/usbmisc")
    : IUsbPrinterPort
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for the answer to a status request.</summary>
    public TimeSpan StatusTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long to wait for the printer to say it started a job; it does so at once.</summary>
    public TimeSpan FirstReplyTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a label may take once the printer reports; long ones need a while.</summary>
    public TimeSpan PerLabelTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public IReadOnlyList<UsbPrinterInfo> List()
    {
        if (!Directory.Exists(deviceFolder))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFileSystemEntries(deviceFolder, "lp*")
                .Order(StringComparer.Ordinal)
                .Select(Describe),
        ];
    }

    private UsbPrinterInfo Describe(string devicePath)
    {
        var idFile = Path.Combine(sysFolder, Path.GetFileName(devicePath), "device", "ieee1284_id");
        var id = File.Exists(idFile) ? Ieee1284Id.Parse(File.ReadAllText(idFile)) : null;
        var name = id?.Manufacturer is { } maker && id.Model is { } model
            ? $"{maker} {model}"
            : id?.Model ?? Path.GetFileName(devicePath);

        return new UsbPrinterInfo(devicePath, name, id?.Model);
    }

    public Task<PrinterStatusSnapshot?> ReadStatusAsync(string identifier, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || NotAPrinterDevice(identifier) is not null)
        {
            return Task.FromResult<PrinterStatusSnapshot?>(null);
        }

        return Task.Run(() =>
        {
            using var device = LinuxDevice.Open(identifier, out _);

            // Busy with a job (the device opens once at a time), unplugged, or not ours to open.
            if (device is null || !device.Write(BrotherStatusReply.Request, StatusTimeout, cancellationToken))
            {
                return null;
            }

            var reply = new byte[BrotherStatusReply.Length];
            return device.Read(reply, StatusTimeout, cancellationToken) == reply.Length
                ? BrotherStatusReply.Parse(reply)?.ToSnapshot()
                : null;
        }, cancellationToken);
    }

    public Task<UsbPrintResult> PrintAsync(string identifier, byte[] data, int labelCount, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return ((IUsbPrinterPort)this).PrintAsync(identifier, data, labelCount, cancellationToken);
        }

        if (NotAPrinterDevice(identifier) is { } refusal)
        {
            return Task.FromResult(UsbPrintResult.Failure(refusal));
        }

        return Task.Run(() =>
        {
            using var device = LinuxDevice.Open(identifier, out var error);

            if (device is null)
            {
                return UsbPrintResult.Failure(error switch
                {
                    LinuxDevice.NoSuchFile or LinuxDevice.NoSuchDevice => NotConnected(identifier),
                    LinuxDevice.AccessDenied => NoPermission(identifier),
                    LinuxDevice.Busy => "The printer is busy with another job. Try again when it has finished.",
                    _ => $"{identifier} can't be opened (error {error}).",
                });
            }

            if (!device.Write(data, SendTimeout, cancellationToken))
            {
                return UsbPrintResult.Failure("The printer didn't take the data within 30 seconds. Is it switched on, with the cover closed?");
            }

            // The printer reports each label as it comes out, or the error it stops on. One that
            // says nothing (or nothing more) leaves the job sent but unconfirmed.
            var completed = 0;
            var reply = new byte[BrotherStatusReply.Length];
            var patience = FirstReplyTimeout;

            while (completed < labelCount && device.Read(reply, patience, cancellationToken) == reply.Length)
            {
                if (BrotherStatusReply.Parse(reply) is not { } status)
                {
                    break;
                }

                if (status.StatusType == BrotherStatusReply.ErrorOccurred || status.Problem is not null)
                {
                    return UsbPrintResult.Failure($"The printer reports a problem: {status.Problem ?? "an error"}.");
                }

                if (status.StatusType == BrotherStatusReply.PrintingCompleted)
                {
                    completed++;
                }

                patience = PerLabelTimeout;
            }

            return completed >= labelCount ? UsbPrintResult.Printed() : UsbPrintResult.Sent();
        }, cancellationToken);
    }

    public string? WriteProblem(string identifier)
    {
        if (NotAPrinterDevice(identifier) is { } refusal)
        {
            return refusal;
        }

        try
        {
            // Opening is enough to tell; nothing is written.
            using var device = new FileStream(identifier, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize: 0);
            return null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return NotConnected(identifier);
        }
        catch (UnauthorizedAccessException)
        {
            return NoPermission(identifier);
        }
        catch (IOException ex)
        {
            return $"{identifier} can't be opened: {ex.Message}";
        }
    }

    /// <summary>Only device files in the USB printer folder: the identifier comes from the database.</summary>
    private string? NotAPrinterDevice(string identifier) =>
        Path.GetDirectoryName(identifier) != deviceFolder || !Path.GetFileName(identifier).StartsWith("lp", StringComparison.Ordinal)
            ? $"{identifier} isn't a USB printer device (expected {deviceFolder}/lp…)."
            : null;

    private static string NotConnected(string identifier) =>
        $"The printer isn't connected ({identifier} doesn't exist). Check the USB cable and that it's switched on.";

    /// <summary>In a container the fix isn't the user's groups but how the container was started.</summary>
    private static string NoPermission(string identifier) =>
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true"
            ? $"No permission to write to {identifier}. Start the container as root (the default), so Tapeory can join the " +
              "device's group, or add that group with --group-add when you run it as another user."
            : $"No permission to write to {identifier}. Add your user to the \"lp\" group (sudo usermod -aG lp $USER), then sign out and in again.";

    public async Task<RawSendResult> SendAsync(string identifier, byte[] data, CancellationToken cancellationToken)
    {
        if (NotAPrinterDevice(identifier) is { } refusal)
        {
            return RawSendResult.Failure(refusal);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);

        try
        {
            // The device blocks while the printer is busy; a separate task keeps that off the caller.
            await Task.Run(() =>
            {
                using var device = new FileStream(identifier, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize: 0);
                device.Write(data);
                device.Flush();
            }, timeout.Token).WaitAsync(timeout.Token);

            return RawSendResult.Success();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return RawSendResult.Failure(NotConnected(identifier));
        }
        catch (UnauthorizedAccessException)
        {
            return RawSendResult.Failure(NoPermission(identifier));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RawSendResult.Failure("The printer didn't take the data within 30 seconds. Is it switched on, with the cover closed?");
        }
        catch (IOException ex)
        {
            return RawSendResult.Failure($"Sending to {identifier} failed: {ex.Message}");
        }
    }
}

/// <summary>
/// A device file opened for reading and writing without blocking: a printer that doesn't answer
/// must not hold the device (it opens once at a time) or a thread. .NET's file streams can't wait
/// for a device with a time limit, hence the C library.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxDevice : IDisposable
{
    public const int NoSuchFile = 2, AccessDenied = 13, Busy = 16, NoSuchDevice = 19;

    private const int ReadWrite = 0x2, NonBlocking = 0x800, CloseOnExec = 0x80000;
    private const int TryAgain = 11, Interrupted = 4;
    private const short PollIn = 0x1, PollOut = 0x4;

    private readonly int _descriptor;

    private LinuxDevice(int descriptor) => _descriptor = descriptor;

    public static LinuxDevice? Open(string path, out int error)
    {
        var descriptor = open(path, ReadWrite | NonBlocking | CloseOnExec);
        error = descriptor < 0 ? Marshal.GetLastPInvokeError() : 0;
        return descriptor < 0 ? null : new LinuxDevice(descriptor);
    }

    /// <summary>Writes everything, waiting while the device is busy; false if it doesn't take it in time.</summary>
    public bool Write(ReadOnlySpan<byte> data, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();

        while (data.Length > 0)
        {
            var written = write(_descriptor, ref MemoryMarshal.GetReference(data), data.Length);

            if (written > 0)
            {
                data = data[(int)written..];
            }
            else if (Marshal.GetLastPInvokeError() is not (TryAgain or Interrupted) || !Wait(PollOut, timeout - clock.Elapsed, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Fills the buffer; returns how much arrived before the time ran out or the data ended.</summary>
    public int Read(Span<byte> buffer, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var total = 0;

        while (total < buffer.Length && Wait(PollIn, timeout - clock.Elapsed, cancellationToken))
        {
            var count = read(_descriptor, ref buffer[total], buffer.Length - total);

            if (count > 0)
            {
                total += (int)count;
            }
            else if (count == 0 || Marshal.GetLastPInvokeError() is TryAgain or Interrupted)
            {
                // Nothing yet: a printer's device answers an empty read until its reply is in.
                Thread.Sleep(20);
            }
            else
            {
                break; // the device is gone
            }
        }

        return total;
    }

    /// <summary>Waits until the device can be read or written; in short steps, to notice a cancellation.</summary>
    private bool Wait(short events, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
        {
            var request = new PollRequest { Descriptor = _descriptor, Events = events };
            var step = (int)Math.Clamp((timeout - clock.Elapsed).TotalMilliseconds, 1, 200);

            if (poll(ref request, 1, step) > 0)
            {
                return (request.Returned & events) != 0;
            }
        }

        return false;
    }

    public void Dispose() => close(_descriptor);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollRequest
    {
        public int Descriptor;
        public short Events;
        public short Returned;
    }

#pragma warning disable SYSLIB1054 // source-generated P/Invokes would need unsafe code enabled for the project
    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int descriptor, ref byte buffer, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern nint write(int descriptor, ref byte buffer, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int poll(ref PollRequest requests, uint count, int timeoutMilliseconds);

    [DllImport("libc")]
    private static extern int close(int descriptor);
#pragma warning restore SYSLIB1054
}

/// <summary>The IEEE 1284 device id a printer reports, e.g.
/// "MFG:Brother;CMD:PT-CBP;MDL:PT-P750W;CLS:PRINTER;".</summary>
public sealed record Ieee1284Id(string? Manufacturer, string? Model)
{
    public static Ieee1284Id Parse(string text)
    {
        var fields = text.Trim().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(field => field.Split(':', 2))
            .Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0].Trim().ToUpperInvariant())
            .ToDictionary(group => group.Key, group => group.First()[1].Trim());

        string? Field(params string[] keys) => keys.Select(key => fields.GetValueOrDefault(key)).FirstOrDefault(value => !string.IsNullOrEmpty(value));

        return new Ieee1284Id(Field("MFG", "MANUFACTURER"), Field("MDL", "MODEL"));
    }
}
