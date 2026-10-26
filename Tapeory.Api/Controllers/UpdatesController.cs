using Tapeory.Api.Settings;
using Tapeory.Api.Updates;
using Microsoft.AspNetCore.Mvc;

namespace Tapeory.Api.Controllers;

[ApiController]
[Route("api/updates")]
public sealed class UpdatesController(UpdateChecker updates, AppSettingsService settings) : ControllerBase
{
    /// <summary>Compares this version with Tapeory's newest release on GitHub: the latest full
    /// release, or also pre-releases if the settings say so.</summary>
    /// <param name="refresh">Ask GitHub again instead of using the cached answer.</param>
    [HttpGet]
    public async Task<IActionResult> Check([FromQuery] bool refresh, CancellationToken cancellationToken)
    {
        var preReleases = (await settings.GetAsync(cancellationToken)).PreReleases;
        return Ok(await updates.CheckAsync(refresh, cancellationToken, preReleases));
    }
}
