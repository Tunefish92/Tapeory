using Tapeory.Api.Desktop;
using Tapeory.Api.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tapeory.Api.Controllers;

/// <summary>
/// First-run setup. These endpoints work before a database exists, and the two that accept
/// connection details refuse once one is configured — otherwise anyone who can reach the app
/// could point it at a different database.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/setup")]
public sealed class SetupController(DatabaseConfigStore store, DatabaseSetupService setup, IConfiguration configuration) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult GetStatus() =>
        Ok(new SetupStatusResponse(store.IsConfigured, DesktopGuard.IsDesktop(configuration)));

    /// <summary>The desktop app's "On this computer": a local database, no server needed.</summary>
    [HttpPost("database/local")]
    public async Task<IActionResult> ConfigureLocalDatabase(CancellationToken cancellationToken)
    {
        if (!DesktopGuard.IsDesktop(configuration))
        {
            return NotFound();
        }

        var result = await setup.ConfigureSqliteAsync(cancellationToken);

        return result.Outcome switch
        {
            DatabaseSetupOutcome.Configured => Ok(new SetupStatusResponse(true, Desktop: true)),
            DatabaseSetupOutcome.AlreadyConfigured => AlreadyConfigured(),
            _ => Problem(title: "Could not create the local database.", detail: result.ErrorMessage, statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPost("database/test")]
    public async Task<IActionResult> TestDatabase(DatabaseSetupRequest request, CancellationToken cancellationToken)
    {
        if (store.IsConfigured)
        {
            return AlreadyConfigured();
        }

        var settings = request.ToSettings();
        var errors = settings.Validate();

        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        return Ok(await setup.TestAsync(settings, cancellationToken));
    }

    [HttpPost("database")]
    public async Task<IActionResult> ConfigureDatabase(DatabaseSetupRequest request, CancellationToken cancellationToken)
    {
        if (store.IsConfigured)
        {
            return AlreadyConfigured();
        }

        var settings = request.ToSettings();
        var errors = settings.Validate();

        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        var result = await setup.ConfigureAsync(settings, cancellationToken);

        return result.Outcome switch
        {
            DatabaseSetupOutcome.Configured => Ok(new SetupStatusResponse(true)),
            DatabaseSetupOutcome.AlreadyConfigured => AlreadyConfigured(),
            _ => Problem(
                title: "Could not set up the database.",
                detail: result.ErrorMessage,
                statusCode: StatusCodes.Status400BadRequest)
        };
    }

    private ObjectResult AlreadyConfigured() => Problem(
        title: "The database is already set up.",
        detail: $"To change the connection, edit or delete {DatabaseConfigStore.ConfigDirectoryName}/{DatabaseConfigStore.FileName} in the storage folder (or change ConnectionStrings__Default, if set) and restart Tapeory.",
        statusCode: StatusCodes.Status409Conflict);
}
