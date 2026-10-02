using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printers;
using Tapeory.Api.Printing;
using Tapeory.Api.Rendering;
using Tapeory.Api.Auth;
using Tapeory.Api.Instances;
using Tapeory.Api.Printing.Usb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tapeory.Api.Controllers;

[ApiController]
[Route("api/printers")]
public sealed class PrintersController(
    PrinterService printers,
    PrinterConnectionTester connectionTester,
    BrotherPrinterDriver driver,
    IPrinterStatusReader statusReader,
    IppClient ipp,
    LabelRenderer renderer,
    TapeoryInstance instance,
    IUsbPrinterPort usb) : ControllerBase
{
    private const int MaxNameLength = 200;
    private const int MaxQueueNameLength = 127;

    [HttpGet]
    public async Task<IActionResult> GetPrinters(CancellationToken cancellationToken)
    {
        var results = await printers.ListAsync(cancellationToken);
        return Ok(results.Select(printer => PrinterMapper.ToResponse(printer, instance)));
    }

    /// <summary>Printers connected to this computer by USB (on Windows: the printers installed in
    /// Windows), for the printer form.</summary>
    [HttpGet("usb")]
    public IActionResult ListUsbPrinters() => Ok(usb.List());

    /// <summary>The margins of a label of this size that can't be printed on, for the editor's
    /// overlay. They belong to the tape or roll (<paramref name="media"/>, or the one of this
    /// height), whichever printer prints it.</summary>
    [HttpGet("print-area")]
    public IActionResult GetPrintArea([FromQuery] decimal widthMm, [FromQuery] decimal heightMm, [FromQuery] string? media)
    {
        if (widthMm <= 0 || heightMm <= 0 || widthMm > 2000 || heightMm > 2000)
        {
            return Problem("The label size isn't valid.", statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(PrintArea.For(widthMm, heightMm, media));
    }

    [HttpGet("models")]
    public IActionResult GetModels() => Ok(BrotherCatalog.Models.Select(model => new PrinterModelResponse(
        model.Name,
        model.Family.ToString(),
        model.Network,
        model.Dpi,
        model.HighResolution,
        model.TwoColor,
        PrinterCapabilities.CutModes(model.Name).Select(mode => mode.ToString()).ToList())));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetPrinter(int id, CancellationToken cancellationToken)
    {
        var printer = await printers.GetByIdAsync(id, cancellationToken);
        return printer is null ? NotFound() : Ok(PrinterMapper.ToResponse(printer, instance));
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.AdminOrOpen)]
    public async Task<IActionResult> CreatePrinter(
        [FromBody] CreatePrinterRequest request, CancellationToken cancellationToken)
    {
        var validationError = Validate(
            request.Name, request.ConnectionType, request.Address, request.PrintServerAddress, request.Port,
            request.QueueName, request.UsbIdentifier);

        if (validationError is not null)
        {
            return Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
        }

        var printer = await printers.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetPrinter), new { id = printer.Id }, PrinterMapper.ToResponse(printer, instance));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthPolicies.AdminOrOpen)]
    public async Task<IActionResult> UpdatePrinter(
        int id, [FromBody] UpdatePrinterRequest request, CancellationToken cancellationToken)
    {
        var validationError = Validate(
            request.Name, request.ConnectionType, request.Address, request.PrintServerAddress, request.Port,
            request.QueueName, request.UsbIdentifier);

        if (validationError is not null)
        {
            return Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
        }

        var printer = await printers.UpdateAsync(id, request, cancellationToken);

        return printer is null ? NotFound() : Ok(PrinterMapper.ToResponse(printer, instance));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthPolicies.AdminOrOpen)]
    public async Task<IActionResult> DeletePrinter(int id, CancellationToken cancellationToken)
    {
        var deleted = await printers.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPut("{id:int}/default")]
    [Authorize(Policy = AuthPolicies.AdminOrOpen)]
    public async Task<IActionResult> SetDefaultPrinter(int id, CancellationToken cancellationToken)
    {
        var success = await printers.SetDefaultAsync(id, cancellationToken);
        return success ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/test-connection")]
    [Authorize(Policy = AuthPolicies.AdminOrOpen)]
    public async Task<IActionResult> TestConnection(int id, CancellationToken cancellationToken)
    {
        var printer = await printers.GetByIdAsync(id, cancellationToken);

        if (printer is null)
        {
            return NotFound();
        }

        if (OnAnotherComputer(printer) is { } elsewhere)
        {
            return elsewhere;
        }

        var result = await connectionTester.TestAsync(printer, cancellationToken);
        await printers.RecordConnectionResultAsync(printer, result, cancellationToken);
        var status = result.IsSuccess ? await ReadStatusAsync(printer, cancellationToken) : null;

        return Ok(new TestConnectionResponse(result.IsSuccess, result.ErrorMessage, status?.LoadedTapeMm));
    }

    /// <summary>The printer's current state over SNMP, e.g. which tape is loaded, so the print form
    /// can warn before a label goes onto the wrong tape.</summary>
    [HttpGet("{id:int}/status")]
    public async Task<IActionResult> GetStatus(int id, CancellationToken cancellationToken)
    {
        var printer = await printers.GetByIdAsync(id, cancellationToken);

        if (printer is null)
        {
            return NotFound();
        }

        if (OnAnotherComputer(printer) is { } elsewhere)
        {
            return elsewhere;
        }

        var status = await ReadStatusAsync(printer, cancellationToken);

        return Ok(status is null
            ? new PrinterStatusResponse(false, null, null, null)
            : new PrinterStatusResponse(true, status.LoadedTapeMm, status.Display.Trim(), status.BlockingError));
    }

    /// <summary>The printer's own status over SNMP: directly for a printer reached by address, or,
    /// behind a CUPS queue, at the device address the queue prints to. A raw print server without
    /// a queue, or a queue whose printer has no network address, can't be asked.</summary>
    private async Task<PrinterStatusSnapshot?> ReadStatusAsync(Printer printer, CancellationToken cancellationToken)
    {
        if (printer is { ConnectionType: PrinterConnectionType.Usb, UsbIdentifier: { } device })
        {
            return await usb.ReadStatusAsync(device, cancellationToken);
        }

        if (PrinterNetworkResolver.Resolve(printer) is not { } target)
        {
            return null;
        }

        if (printer.ConnectionType is PrinterConnectionType.IpAddress or PrinterConnectionType.Hostname)
        {
            return await statusReader.ReadAsync(target.Host, cancellationToken);
        }

        if (printer is not { ConnectionType: PrinterConnectionType.PrintServer, QueueName: { } queue })
        {
            return null;
        }

        try
        {
            var attributes = await ipp.GetPrinterAttributesAsync(IppClient.QueueUri(target.Host, target.Port, queue), cancellationToken);
            return IppClient.DeviceHost(attributes.Text("device-uri")) is { } host
                ? await statusReader.ReadAsync(host, cancellationToken)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException
                                       or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Renders a small built-in test label, prints it, and waits until the printer
    /// confirms it came out (or reports why it didn't).</summary>
    [HttpPost("{id:int}/test-print")]
    [Authorize(Policy = AuthPolicies.AdminOrOpen)]
    public async Task<IActionResult> TestPrint(int id, CancellationToken cancellationToken)
    {
        var printer = await printers.GetByIdAsync(id, cancellationToken);

        if (printer is null)
        {
            return NotFound();
        }

        if (OnAnotherComputer(printer) is { } elsewhere)
        {
            return elsewhere;
        }

        // Without a size of its own, the test label is as high as the tape the printer reports, so
        // it prints on whatever is loaded. A guess isn't sent: a job for the wrong tape width
        // leaves the printer waiting with an error until someone cancels it there.
        var loadedTapeMm = printer.LabelMediaHeightMm is null
            ? (await ReadStatusAsync(printer, cancellationToken))?.LoadedTapeMm
            : null;

        if (printer.LabelMediaHeightMm is null && loadedTapeMm is null)
        {
            return Ok(new TestPrintResponse(
                false,
                "Tapeory can't tell which tape is in this printer. Edit the printer and set the test print size: " +
                "its height is the tape's width, e.g. 12 mm."));
        }

        var widthMm = printer.LabelMediaWidthMm ?? 50m;
        var heightMm = printer.LabelMediaHeightMm ?? loadedTapeMm!.Value;
        var model = BrotherCatalog.Find(printer.Model);
        var testDocument = TestLabel.Build(widthMm, heightMm, model);
        var resolution = PrinterCapabilities.Resolve(printer.Model, PrintQuality.Standard);
        var (label, media) = BrotherLabelRaster.Render(
            renderer, testDocument, new Dictionary<string, string>(), _ => null, model, resolution);

        using var _ = label;
        var result = await driver.PrintAsync(
            printer, [label], media, resolution, PrinterCapabilities.CutModes(printer.Model)[0],
            _ => Task.CompletedTask, cancellationToken);
        await printers.RecordConnectionResultAsync(
            printer, new ConnectionTestResult(result.IsSuccess, result.ErrorMessage), cancellationToken);

        return Ok(new TestPrintResponse(result.IsSuccess, result.ErrorMessage));
    }

    private static string? Validate(
        string? name, string? connectionType, string? address, string? printServerAddress, int? port,
        string? queueName, string? usbIdentifier)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name is required.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"Name must be {MaxNameLength} characters or fewer.";
        }

        if (!Enum.TryParse<PrinterConnectionType>(connectionType, true, out var parsedType))
        {
            return $"Unknown connection type '{connectionType}'.";
        }

        var isIpOrHostname = parsedType is PrinterConnectionType.IpAddress or PrinterConnectionType.Hostname;

        if (isIpOrHostname && string.IsNullOrWhiteSpace(address))
        {
            return "Address is required for this connection type.";
        }

        if (parsedType == PrinterConnectionType.PrintServer && string.IsNullOrWhiteSpace(printServerAddress))
        {
            return "Print server address is required for this connection type.";
        }

        if (parsedType == PrinterConnectionType.Usb && string.IsNullOrWhiteSpace(usbIdentifier))
        {
            return "Choose the USB printer (its device on Linux, its name in Windows).";
        }

        if (port is < 1 or > 65535)
        {
            return "Port must be between 1 and 65535.";
        }

        // A common mix-up: pasting a URL or CUPS device address (socket://…, ipp://…/printers/…).
        foreach (var host in new[] { isIpOrHostname ? address : null, parsedType == PrinterConnectionType.PrintServer ? printServerAddress : null })
        {
            if (host is not null && (host.Contains("://") || host.Contains('/') || host.Trim().Contains(' ')))
            {
                return $"Enter just the host name or IP address (e.g. 10.0.0.10), not '{host.Trim()}'. "
                       + "The port and, for a CUPS server, the queue name have their own fields.";
            }
        }

        if (queueName is { Length: > MaxQueueNameLength } || queueName?.IndexOfAny(['/', '?', '#']) >= 0)
        {
            return $"Queue name must be {MaxQueueNameLength} characters or fewer, without '/', '?' or '#'.";
        }

        return null;
    }

    /// <summary>A USB printer connected to another computer can only be used from Tapeory there.</summary>
    private ObjectResult? OnAnotherComputer(Printer printer) =>
        instance.Owns(printer.InstanceId)
            ? null
            : Problem(
                $"This printer is connected to {printer.ComputerName ?? "another computer"}. Use Tapeory on that computer to print to it.",
                statusCode: StatusCodes.Status409Conflict);
}
