using Microsoft.AspNetCore.Mvc;
using Tapeory.Api.Barcodes;

namespace Tapeory.Api.Controllers;

public sealed record EncodeBarcodeRequest(string? Symbology, string? Data);

/// <param name="Modules">Row by row, "1" for a dark module and "0" for a light one.</param>
/// <param name="Text">What prints under a 1D barcode's bars.</param>
public sealed record EncodedBarcodeResponse(int Columns, int Rows, bool TwoDimensional, string Modules, string Text);

/// <summary>Encodes barcodes for the editor, so it draws exactly the module grid the renderer
/// prints.</summary>
[ApiController]
[Route("api/barcodes")]
public sealed class BarcodesController : ControllerBase
{
    [HttpPost("encode")]
    public IActionResult Encode([FromBody] EncodeBarcodeRequest request)
    {
        var symbology = request.Symbology ?? "";
        var data = request.Data ?? "";
        var result = BarcodeEncoder.Encode(symbology, data);

        if (result.Matrix is not { } matrix)
        {
            return Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(new EncodedBarcodeResponse(
            matrix.Columns,
            matrix.Rows,
            matrix.IsTwoDimensional,
            string.Concat(matrix.Modules.Select(dark => dark ? '1' : '0')),
            BarcodeLayout.DisplayText(symbology, data)));
    }
}
