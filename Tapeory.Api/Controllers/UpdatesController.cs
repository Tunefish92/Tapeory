using Tapeory.Api.Data;
using Tapeory.Api.Settings;
using Tapeory.Api.Updates;
using Microsoft.AspNetCore.Mvc;

namespace Tapeory.Api.Controllers;

[ApiController]
[Route("api/updates")]
public sealed class UpdatesController(UpdateChecker updates, AppSettingsService settings, AppDbContext db) : ControllerBase
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

    /// <summary>The version that runs, and whether it was installed as an update (so the web app
    /// can show what's new once).</summary>
    [HttpGet("installed")]
    public async Task<IActionResult> Installed(CancellationToken cancellationToken) =>
        Ok(await InstalledVersion.GetAsync(db, UpdateChecker.CurrentVersion, cancellationToken));
}
