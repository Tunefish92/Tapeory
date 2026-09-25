using Tapeory.Api.Rendering;
using Microsoft.AspNetCore.Mvc;

namespace Tapeory.Api.Controllers;

[ApiController]
[Route("api/fonts")]
public sealed class FontsController(FontCatalog fonts) : ControllerBase
{
    /// <summary>Font families installed on the server that labels can be rendered with.</summary>
    [HttpGet]
    public IActionResult List() => Ok(fonts.Families);

    /// <summary>
    /// The font file itself, so the browser editor previews with exactly the font the printed
    /// label uses. Only families from <see cref="List"/> are served.
    /// </summary>
    [HttpGet("file")]
    public IActionResult File([FromQuery] string family, [FromQuery] string? weight)
    {
        if (fonts.Find(family) is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Font not installed.");
        }

        var file = fonts.GetFontFile(family, string.Equals(weight, "bold", StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Font file unavailable.");
        }

        // Installed fonts change rarely; a day of browser caching keeps the editor snappy.
        Response.Headers.CacheControl = "private, max-age=86400";
        return base.File(file.Data, file.ContentType);
    }
}
