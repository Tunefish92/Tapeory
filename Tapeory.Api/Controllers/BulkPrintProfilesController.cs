using Microsoft.AspNetCore.Mvc;
using Tapeory.Api.Auth;
using Tapeory.Api.PrintData;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Controllers;

/// <summary>Saved bulk print setups of the signed-in account.</summary>
[ApiController]
public sealed class BulkPrintProfilesController(BulkPrintProfileService profiles, TemplateService templates) : ControllerBase
{
    [HttpGet("api/templates/{templateId:int}/bulk-print-profiles")]
    public async Task<IActionResult> List(int templateId, CancellationToken cancellationToken)
    {
        if (await templates.GetByIdAsync(templateId, cancellationToken) is null)
        {
            return NotFound();
        }

        return Ok(await profiles.ListAsync(templateId, AuthClaims.UserId(User), cancellationToken));
    }

    /// <summary>Saves a profile. A name that is already taken answers 409, unless the request
    /// says to replace that profile.</summary>
    [HttpPut("api/templates/{templateId:int}/bulk-print-profiles")]
    public async Task<IActionResult> Save(int templateId, [FromBody] SaveBulkPrintProfileRequest request, CancellationToken cancellationToken)
    {
        if (await templates.GetByIdAsync(templateId, cancellationToken) is null)
        {
            return NotFound();
        }

        var result = await profiles.SaveAsync(templateId, AuthClaims.UserId(User), request, cancellationToken);

        return result.Profile is not null
            ? Ok(result.Profile)
            : Problem(result.Error, statusCode: result.NameTaken ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
    }

    [HttpDelete("api/bulk-print-profiles/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        await profiles.DeleteAsync(id, AuthClaims.UserId(User), cancellationToken) ? NoContent() : NotFound();
}
