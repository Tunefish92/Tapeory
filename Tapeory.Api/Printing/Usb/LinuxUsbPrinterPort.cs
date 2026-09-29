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

    public async Task<RawSendResult> SendAsync(string identifier, byte[] data, CancellationToken cancellationToken)
    {
        // Only device files in the USB printer folder: the identifier comes from the database.
        if (Path.GetDirectoryName(identifier) != deviceFolder || !Path.GetFileName(identifier).StartsWith("lp", StringComparison.Ordinal))
        {
            return RawSendResult.Failure($"{identifier} isn't a USB printer device (expected {deviceFolder}/lp…).");
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
            return RawSendResult.Failure($"The printer isn't connected ({identifier} doesn't exist). Check the USB cable and that it's switched on.");
        }
        catch (UnauthorizedAccessException)
        {
            return RawSendResult.Failure(
                $"No permission to write to {identifier}. Add your user to the \"lp\" group (sudo usermod -aG lp $USER), then sign out and in again.");
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
