using Tapeory.Api.Stats;
using Tapeory.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tapeory.Api.Controllers;

[ApiController]
[Route("api/stats")]
public sealed class StatsController(StatsService stats) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetDashboardStats(CancellationToken cancellationToken) =>
        Ok(await stats.GetDashboardStatsAsync(cancellationToken));

    /// <summary>Starts the print statistics from zero. Print history is kept.</summary>
    [HttpPost("reset")]
    [Authorize(Policy = AuthPolicies.AdminOrOpen)]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken) =>
        Ok(await stats.ResetAsync(cancellationToken));

    /// <summary>Undoes a reset, going back to all-time statistics.</summary>
    [HttpDelete("reset")]
    [Authorize(Policy = AuthPolicies.AdminOrOpen)]
    public async Task<IActionResult> RestoreAllTime(CancellationToken cancellationToken) =>
        Ok(await stats.RestoreAllTimeAsync(cancellationToken));
}
