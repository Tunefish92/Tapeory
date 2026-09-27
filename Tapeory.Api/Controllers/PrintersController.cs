using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printers;
using Tapeory.Api.Printing;
using Tapeory.Api.Rendering;
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
    LabelRenderer renderer) : ControllerBase
{
    private const int MaxNameLength = 200;
    private const int MaxQueueNameLength = 127;

    [HttpGet]
    public async Task<IActionResult> GetPrinters(CancellationToken cancellationToken)
    {
        var results = await printers.ListAsync(cancellationToken);
        return Ok(results.Select(PrinterMapper.ToResponse));
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
        return printer is null ? NotFound() : Ok(PrinterMapper.ToResponse(printer));
    }

    [HttpPost]
    public async Task<IActionResult> CreatePrinter(
        [FromBody] CreatePrinterRequest request, CancellationToken cancellationToken)
    {
        var validationError = Validate(
            request.Name, request.ConnectionType, request.Address, request.PrintServerAddress, request.Port,
            request.QueueName);

        if (validationError is not null)
        {
            return Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
        }

        var printer = await printers.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetPrinter), new { id = printer.Id }, PrinterMapper.ToResponse(printer));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdatePrinter(
        int id, [FromBody] UpdatePrinterRequest request, CancellationToken cancellationToken)
    {
        var validationError = Validate(
            request.Name, request.ConnectionType, request.Address, request.PrintServerAddress, request.Port,
            request.QueueName);

        if (validationError is not null)
        {
            return Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
        }

        var printer = await printers.UpdateAsync(id, request, cancellationToken);

        return printer is null ? NotFound() : Ok(PrinterMapper.ToResponse(printer));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeletePrinter(int id, CancellationToken cancellationToken)
    {
        var deleted = await printers.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPut("{id:int}/default")]
    public async Task<IActionResult> SetDefaultPrinter(int id, CancellationToken cancellationToken)
    {
        var success = await printers.SetDefaultAsync(id, cancellationToken);
        return success ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/test-connection")]
    public async Task<IActionResult> TestConnection(int id, CancellationToken cancellationToken)
    {
        var printer = await printers.GetByIdAsync(id, cancellationToken);

        if (printer is null)
        {
            return NotFound();
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
    public async Task<IActionResult> TestPrint(int id, CancellationToken cancellationToken)
    {
        var printer = await printers.GetByIdAsync(id, cancellationToken);

        if (printer is null)
        {
            return NotFound();
        }

        var widthMm = printer.LabelMediaWidthMm ?? 50m;
        var heightMm = printer.LabelMediaHeightMm ?? 25m;
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
        string? queueName)
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
}
