using System.Security.Claims;
using Tapeory.Api.Auth;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Templates;

/// <summary>
/// Who may see and change which templates. Private templates are seen only by their owner and
/// administrators; public ones by everyone. Only the owner and administrators change a template.
/// A request without an account only gets this far while Tapeory has no accounts at all (the
/// authorization policy stops it otherwise), and then everything is open, as before accounts.
/// </summary>
public sealed record TemplateAccess(int? UserId, bool IsAdmin)
{
    public static TemplateAccess For(ClaimsPrincipal user) =>
        AuthClaims.UserId(user) is { } id
            ? new TemplateAccess(id, user.IsInRole(nameof(UserRole.Admin)))
            : new TemplateAccess(null, IsAdmin: true);

    public bool CanView(Template template) =>
        IsAdmin || template.IsPublic || (UserId is not null && template.OwnerUserId == UserId);

    public bool CanEdit(Template template) =>
        IsAdmin || (UserId is not null && template.OwnerUserId == UserId);

    /// <summary>The same rule as <see cref="CanView"/>, for database queries.</summary>
    public IQueryable<Template> Visible(IQueryable<Template> templates) =>
        IsAdmin ? templates : templates.Where(template => template.IsPublic || template.OwnerUserId == UserId);

    /// <summary>The same rule as <see cref="CanEdit"/>, for database queries.</summary>
    public IQueryable<Template> Editable(IQueryable<Template> templates) =>
        IsAdmin ? templates : templates.Where(template => template.OwnerUserId == UserId);

    /// <summary>Private and owned by the creator; ownerless and public while there are no accounts.</summary>
    public void ClaimNew(Template template)
    {
        template.OwnerUserId = UserId;
        template.IsPublic = UserId is null;
    }
}
