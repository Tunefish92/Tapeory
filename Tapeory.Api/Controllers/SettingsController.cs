using Tapeory.Api.Settings;
using Microsoft.AspNetCore.Mvc;

namespace Tapeory.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController(AppSettingsService settings) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(await settings.GetAsync(cancellationToken));

    /// <summary>Partial update — omitted (null) properties keep their current value.</summary>
    [HttpPut]
    public async Task<IActionResult> Update(UpdateAppSettingsRequest request, CancellationToken cancellationToken)
    {
        var errors = AppSettingsService.Validate(request);

        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        return Ok(await settings.UpdateAsync(request, cancellationToken));
    }
}
