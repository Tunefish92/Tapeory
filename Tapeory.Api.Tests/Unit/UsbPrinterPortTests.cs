using Tapeory.Api.Printing.Usb;

namespace Tapeory.Api.Tests.Unit;

/// <summary>The Linux USB port, against a fake /dev/usb and /sys/class/usbmisc.</summary>
public sealed class UsbPrinterPortTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("tapeory-usb-").FullName;
    private readonly string _devices;
    private readonly string _sys;

    public UsbPrinterPortTests()
    {
        _devices = Path.Combine(_root, "dev", "usb");
        _sys = Path.Combine(_root, "sys");
        Directory.CreateDirectory(_devices);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string AddPrinter(string device, string? ieee1284Id)
    {
        var path = Path.Combine(_devices, device);
        File.WriteAllBytes(path, []);

        if (ieee1284Id is not null)
        {
            var idFolder = Path.Combine(_sys, device, "device");
            Directory.CreateDirectory(idFolder);
            File.WriteAllText(Path.Combine(idFolder, "ieee1284_id"), ieee1284Id);
        }

        return path;
    }

    private LinuxUsbPrinterPort Port() => new(_devices, _sys);

    [Fact]
    public void Lists_TheConnectedPrinters_WithTheModelTheyReport()
    {
        AddPrinter("lp1", "MFG:Brother;CMD:PT-CBP;MDL:PT-P750W;CLS:PRINTER;");
        AddPrinter("lp0", null);

        var printers = Port().List();

        Assert.Equal(2, printers.Count);
        Assert.Equal(new UsbPrinterInfo(Path.Combine(_devices, "lp0"), "lp0", null), printers[0]);
        Assert.Equal(new UsbPrinterInfo(Path.Combine(_devices, "lp1"), "Brother PT-P750W", "PT-P750W"), printers[1]);
    }

    [Fact]
    public void ListsNothing_WithoutAUsbPrinterFolder()
    {
        Assert.Empty(new LinuxUsbPrinterPort(Path.Combine(_root, "nothing-here"), _sys).List());
    }

    [Fact]
    public async Task Send_WritesTheDataToTheDevice()
    {
        var device = AddPrinter("lp0", null);

        var result = await Port().SendAsync(device, [0x1B, 0x40, 0x0C], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new byte[] { 0x1B, 0x40, 0x0C }, File.ReadAllBytes(device));
    }

    [Fact]
    public async Task Send_ExplainsAMissingDevice()
    {
        var result = await Port().SendAsync(Path.Combine(_devices, "lp3"), [1], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("isn't connected", result.ErrorMessage);
    }

    [Fact]
    public async Task Send_ExplainsMissingPermission_WithTheLpGroup()
    {
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root")
        {
            return; // root can write anyway
        }

        var device = AddPrinter("lp0", null);
        File.SetUnixFileMode(device, UnixFileMode.None);

        var result = await Port().SendAsync(device, [1], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("\"lp\" group", result.ErrorMessage);
    }

    [Fact]
    public async Task Send_OnlyWritesToUsbPrinterDevices()
    {
        var result = await Port().SendAsync(Path.Combine(_root, "somewhere-else"), [1], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(Path.Combine(_root, "somewhere-else")));
    }

    [Theory]
    [InlineData("MFG:Brother;CMD:PT-CBP;MDL:PT-P750W;CLS:PRINTER;", "Brother", "PT-P750W")]
    [InlineData("MANUFACTURER:Brother;COMMAND SET:PT-CBP;MODEL:QL-820NWB;", "Brother", "QL-820NWB")]
    [InlineData("garbage", null, null)]
    public void ParsesTheDeviceId(string text, string? manufacturer, string? model)
    {
        Assert.Equal(new Ieee1284Id(manufacturer, model), Ieee1284Id.Parse(text));
    }

    // WindowsUsbPrinterPort is switched off for now, see Tapeory.Api.csproj.
#if WINDOWS_USB
    [Theory]
    [InlineData("Brother PT-P750W", "PT-P750W")]
    [InlineData("Brother QL-820NWB (Copy 1)", "QL-820NWB")]
    [InlineData("Microsoft Print to PDF", null)]
    public void FindsTheModel_InAWindowsPrinterName(string name, string? model)
    {
        Assert.Equal(model, WindowsUsbPrinterPort.ModelIn(name));
    }
#endif
}
