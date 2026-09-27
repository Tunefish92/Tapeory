using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tapeory.Api.Auth;

namespace Tapeory.Api.Controllers;

/// <summary>Managing accounts: administrators only.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthPolicies.Admin)]
public sealed class UsersController(AccountService accounts) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok((await accounts.ListAsync(cancellationToken)).Select(UserResponse.From));

    /// <summary>Creates an account with a temporary password, which it changes when first signing in.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.CreateAsync(request, cancellationToken);

        return result.Succeeded
            ? Ok(new TemporaryPasswordResponse(UserResponse.From(result.Value.User), result.Value.Password))
            : Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.UpdateAsync(id, CurrentUserId, request, cancellationToken);
        return result.Succeeded ? Ok(UserResponse.From(result.Value!)) : Failure(result);
    }

    /// <summary>Sets a temporary password and ends the account's sessions.</summary>
    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, CancellationToken cancellationToken)
    {
        var result = await accounts.ResetPasswordAsync(id, cancellationToken);

        return result.Succeeded
            ? Ok(new TemporaryPasswordResponse(UserResponse.From(result.Value.User), result.Value.Password))
            : Failure(result);
    }

    /// <param name="templates">"transfer" (default): the account's templates become yours;
    /// "delete": they're deleted with it.</param>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] string? templates, CancellationToken cancellationToken)
    {
        if (templates is not (null or "transfer" or "delete"))
        {
            return Problem(title: "templates must be transfer or delete.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await accounts.DeleteAsync(id, CurrentUserId, cancellationToken, deleteTemplates: templates == "delete");
        return result.Succeeded ? NoContent() : Failure(result);
    }

    private int CurrentUserId => AuthClaims.UserId(User)!.Value;

    private ObjectResult Failure<T>(AccountResult<T> result) =>
        Problem(title: result.Message, statusCode: AccountErrors.StatusCode(result.Error));
}
