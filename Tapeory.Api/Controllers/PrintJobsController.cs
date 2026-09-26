using Tapeory.Api.Data;
using Tapeory.Api.PrintJobs;
using Tapeory.Api.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Tapeory.Api.Controllers;

[ApiController]
[Route("api/print-jobs")]
public sealed class PrintJobsController(PrintJobService printJobs, AppDbContext db, FileStorageService fileStorage)
    : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreatePrintJob(
        [FromBody] CreatePrintJobRequest request,
        CancellationToken cancellationToken)
    {
        var result = await printJobs.CreateAsync(request, cancellationToken);

        if (result.TemplateNotFound)
        {
            return Problem(
                $"Template {request.TemplateId} was not found.", statusCode: StatusCodes.Status404NotFound);
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
        var item = await db.PrintJobItems
            .Include(i => i.RenderedImageFile)
            .SingleOrDefaultAsync(i => i.Id == itemId, cancellationToken);

        if (item?.RenderedImageFile is null)
        {
            return NotFound();
        }

        var stream = fileStorage.OpenRead(item.RenderedImageFile.RelativePath);
        return File(stream, "image/png");
    }
}
