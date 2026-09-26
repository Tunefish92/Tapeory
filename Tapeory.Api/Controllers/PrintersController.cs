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
    PrinterRawSocketSender rawSender,
    LabelRenderer renderer) : ControllerBase
{
    private const int MaxNameLength = 200;

    [HttpGet]
    public async Task<IActionResult> GetPrinters(CancellationToken cancellationToken)
    {
        var results = await printers.ListAsync(cancellationToken);
        return Ok(results.Select(PrinterMapper.ToResponse));
    }

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
            request.Name, request.ConnectionType, request.Address, request.PrintServerAddress, request.Port);

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
            request.Name, request.ConnectionType, request.Address, request.PrintServerAddress, request.Port);

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

        return Ok(new TestConnectionResponse(result.IsSuccess, result.ErrorMessage));
    }

    /// <summary>Renders a small built-in test label and sends it to the printer's raw socket.
    /// See PrinterRawSocketSender's remarks: this proves the network path works, not that a real
    /// Brother printer will render the result correctly.</summary>
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
        var testDocument = BuildTestLabelDocument(widthMm, heightMm);
        var pngBytes = renderer.RenderPng(testDocument, new Dictionary<string, string>(), _ => null);

        var result = await rawSender.SendAsync(printer, pngBytes, cancellationToken);
        await printers.RecordConnectionResultAsync(
            printer, new ConnectionTestResult(result.IsSuccess, result.ErrorMessage), cancellationToken);

        return Ok(new TestPrintResponse(result.IsSuccess, result.ErrorMessage));
    }

    private static RenderableDocument BuildTestLabelDocument(decimal widthMm, decimal heightMm) => new(
        widthMm,
        heightMm,
        [
            new RenderableText(
                2, 2, 0, false, false,
                "Tapeory Test Print", Math.Max(widthMm - 4, 1), Math.Max(heightMm - 4, 1),
                10, "Arial", "normal", "left", "#000000")
        ]);

    private static string? Validate(
        string? name, string? connectionType, string? address, string? printServerAddress, int? port)
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

        return null;
    }
}
