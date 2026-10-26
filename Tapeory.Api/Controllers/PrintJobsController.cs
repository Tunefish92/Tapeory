using Tapeory.Api.Auth;
using Tapeory.Api.PrintJobs;
using Tapeory.Api.Storage;
using Microsoft.AspNetCore.Mvc;

namespace Tapeory.Api.Controllers;

[ApiController]
[Route("api/print-jobs")]
public sealed class PrintJobsController(PrintJobService printJobs, FileStorageService fileStorage)
    : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreatePrintJob(
        [FromBody] CreatePrintJobRequest request,
        CancellationToken cancellationToken)
    {
        return await CreatedAsync(await printJobs.CreateAsync(request, cancellationToken, Author()), cancellationToken);
    }

    /// <summary>Stops a job. A waiting job is cancelled at once; one that is printing stops after
    /// the labels already at the printer. Answers with the job as it is now.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelPrintJob(int id, CancellationToken cancellationToken)
    {
        if (!await printJobs.CancelAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var job = await printJobs.GetByIdAsync(id, cancellationToken);
        return job is null ? NotFound() : Ok(PrintJobMapper.ToResponse(job));
    }

    /// <summary>Creates a new job with the rows of this one that weren't printed.</summary>
    [HttpPost("{id:int}/reprint-unprinted")]
    public async Task<IActionResult> ReprintUnprinted(int id, CancellationToken cancellationToken)
    {
        var result = await printJobs.ReprintUnprintedAsync(id, cancellationToken, Author());
        return result is null ? NotFound() : await CreatedAsync(result, cancellationToken);
    }

    private PrintAuthor? Author() =>
        AuthClaims.UserId(User) is { } userId
            ? new PrintAuthor(userId, User.FindFirst(AuthClaims.DisplayName)?.Value ?? User.Identity!.Name!)
            : null;

    private async Task<IActionResult> CreatedAsync(CreatePrintJobResult result, CancellationToken cancellationToken)
    {
        if (result.TemplateNotFound)
        {
            return Problem("The template was not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (result.Errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["items"] = [.. result.Errors] }));
        }

        var job = await printJobs.GetByIdAsync(result.JobId!.Value, cancellationToken);

        return CreatedAtAction(nameof(GetPrintJob), new { id = job!.Id }, PrintJobMapper.ToResponse(job));
    }

    [HttpGet]
    public async Task<IActionResult> GetPrintJobs([FromQuery] int? templateId, CancellationToken cancellationToken)
    {
        var jobs = await printJobs.ListAsync(templateId, cancellationToken);
        return Ok(jobs.Select(PrintJobMapper.ToResponse));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetPrintJob(int id, CancellationToken cancellationToken)
    {
        var job = await printJobs.GetByIdAsync(id, cancellationToken);
        return job is null ? NotFound() : Ok(PrintJobMapper.ToResponse(job));
    }

    /// <summary>Removes the job from the print history. It keeps counting in the statistics.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeletePrintJob(int id, CancellationToken cancellationToken)
    {
        return await printJobs.DeleteAsync(id, cancellationToken) switch
        {
            DeletePrintJobResult.NotFound => NotFound(),
            DeletePrintJobResult.StillPrinting => Problem(
                "This print job is still printing. Wait until it has finished, then delete it.",
                statusCode: StatusCodes.Status409Conflict),
            _ => NoContent()
        };
    }

    /// <summary>Clears the whole print history, except jobs that are still queued or printing.</summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteAllPrintJobs(CancellationToken cancellationToken) =>
        Ok(await printJobs.DeleteAllAsync(cancellationToken));

    [HttpGet("items/{itemId:int}/preview")]
    public async Task<IActionResult> GetItemPreview(int itemId, CancellationToken cancellationToken)
    {
        var rendered = await printJobs.GetItemPreviewFileAsync(itemId, cancellationToken);

        if (rendered is null)
        {
            return NotFound();
        }

        var stream = fileStorage.OpenRead(rendered.RelativePath);
        return File(stream, "image/png");
    }
}
