using Tapeory.Api.Updates;
using Microsoft.AspNetCore.Mvc;

namespace Tapeory.Api.Controllers;

[ApiController]
[Route("api/updates")]
public sealed class UpdatesController(UpdateChecker updates) : ControllerBase
{
    /// <summary>Compares this version with Tapeory's latest release on GitHub.</summary>
    /// <param name="refresh">Ask GitHub again instead of using the cached answer.</param>
    [HttpGet]
    public async Task<IActionResult> Check([FromQuery] bool refresh, CancellationToken cancellationToken) =>
        Ok(await updates.CheckAsync(refresh, cancellationToken));
}
