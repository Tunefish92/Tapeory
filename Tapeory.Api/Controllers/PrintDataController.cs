using Microsoft.AspNetCore.Mvc;
using Tapeory.Api.PrintData;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Controllers;

/// <summary>Reads the data for bulk printing: an uploaded file (Excel, CSV, text, JSON) or the
/// answer of a web address.</summary>
[ApiController]
[Route("api/print-data")]
public sealed class PrintDataController(TemplateService templates, PrintDataFetcher fetcher) : ControllerBase
{
    /// <summary>Reads the file and matches its columns to the template's fields. Send
    /// <paramref name="separator"/> or <paramref name="sheet"/> to read it again with the user's
    /// choice; <paramref name="locale"/> (e.g. "de-DE") decides how Excel's region-dependent
    /// dates are written.</summary>
    [HttpPost("parse")]
    // Twice the limit gets through to the check below, which says what is wrong; the framework's
    // own refusal of a larger upload doesn't.
    [RequestSizeLimit(PrintDataReader.MaxSizeBytes * 2)]
    [RequestFormLimits(MultipartBodyLengthLimit = PrintDataReader.MaxSizeBytes * 2)]
    public async Task<IActionResult> Parse(
        IFormFile? file,
        [FromForm] int templateId,
        [FromForm] string? separator,
        [FromForm] int? sheet,
        [FromForm] string? locale,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(file is null ? "No file was uploaded." : "The file is empty.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (file.Length > PrintDataReader.MaxSizeBytes)
        {
            return Problem(
                $"The file is larger than {PrintDataReader.MaxSizeBytes / (1024 * 1024)} MB.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var template = await templates.GetByIdAsync(templateId, cancellationToken);

        if (template?.CurrentVersion is null)
        {
            return NotFound();
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        var data = PrintDataReader.Read(
            buffer.ToArray(), string.IsNullOrEmpty(separator) ? null : separator, sheet, DataLocale.For(locale),
            template.CurrentVersion.Fields, out var error);

        return data is null ? Problem(error, statusCode: StatusCodes.Status400BadRequest) : Ok(data);
    }

    /// <summary>Gets the data from a web address (a REST endpoint answering JSON, or a file served
    /// over HTTP) and reads it like an uploaded file. The server makes the request.</summary>
    [HttpPost("fetch")]
    public async Task<IActionResult> Fetch([FromBody] FetchPrintDataRequest request, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(request.TemplateId, cancellationToken);

        if (template?.CurrentVersion is null)
        {
            return NotFound();
        }

        var (content, fetchError) = await fetcher.FetchAsync(request.Url, request.HeaderName, request.HeaderValue, cancellationToken);

        if (content is null)
        {
            return Problem(fetchError, statusCode: StatusCodes.Status400BadRequest);
        }

        var data = PrintDataReader.Read(
            content, string.IsNullOrEmpty(request.Separator) ? null : request.Separator, request.Sheet, DataLocale.For(request.Locale),
            template.CurrentVersion.Fields, out var error);

        return data is null ? Problem(error, statusCode: StatusCodes.Status400BadRequest) : Ok(data);
    }
}
