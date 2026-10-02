using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.PrintData;

/// <param name="Column">The column's position in the file, from 0.</param>
/// <param name="Header">The column's header text when the file has a header row; a file whose
/// columns moved is then still matched by name.</param>
public sealed record BulkPrintProfileColumn(string Field, int Column, string? Header);

/// <summary>Everything a bulk print needs to run again: the file, how it is read, which column
/// fills which field, and how it is printed.</summary>
/// <param name="FilePath">Where the file is on the computer of the desktop app that saved the
/// profile; a browser never knows a file's path.</param>
/// <param name="Separator">As in <see cref="PrintDataResponse.Separator"/>; null for an Excel file.</param>
/// <param name="Url">The web address the data comes from, instead of a file; with an optional
/// request header (<paramref name="UrlHeaderName"/>, <paramref name="UrlHeaderValue"/>).</param>
/// <param name="Copies">How many times each label is printed (on top of a copies column).</param>
public sealed record BulkPrintProfileSettings(
    string? FileName,
    string? FilePath,
    string? Separator,
    int? Sheet,
    bool HasHeader,
    List<BulkPrintProfileColumn>? Columns,
    int? QuantityColumn,
    string? QuantityHeader,
    int? PrinterId,
    string? PrinterName,
    string? Quality,
    string? CutMode,
    int? Copies = null,
    string? Url = null,
    string? UrlHeaderName = null,
    string? UrlHeaderValue = null);

/// <param name="Replace">Overwrite a profile that already has this name; without it, saving under
/// a taken name is refused, so the caller can ask first.</param>
public sealed record SaveBulkPrintProfileRequest(string? Name, BulkPrintProfileSettings? Settings, bool Replace = false);

/// <param name="NameTaken">Refused because a profile with this name exists and replacing wasn't asked for.</param>
public sealed record SaveBulkPrintProfileResult(BulkPrintProfileResponse? Profile, string? Error, bool NameTaken = false);

public sealed record BulkPrintProfileResponse(int Id, int TemplateId, string Name, BulkPrintProfileSettings Settings, DateTimeOffset UpdatedAt);

/// <summary>Saved bulk print setups. Each account has its own per template; a name is used once
/// (ignoring case), and saving under it again replaces that profile when asked to.</summary>
public sealed class BulkPrintProfileService(AppDbContext db)
{
    public const int MaxNameLength = 100;

    /// <summary>Profiles one account can keep per template.</summary>
    public const int MaxPerTemplate = 50;

    private const int MaxSettingsLength = 32 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<BulkPrintProfileResponse>> ListAsync(int templateId, int? ownerUserId, CancellationToken cancellationToken)
    {
        var profiles = await Own(templateId, ownerUserId).ToListAsync(cancellationToken);
        return [.. profiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).Select(ToResponse)];
    }

    public async Task<SaveBulkPrintProfileResult> SaveAsync(
        int templateId, int? ownerUserId, SaveBulkPrintProfileRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            return new SaveBulkPrintProfileResult(null, "Give the profile a name.");
        }

        if (name.Length > MaxNameLength)
        {
            return new SaveBulkPrintProfileResult(null, $"The name can have at most {MaxNameLength} characters.");
        }

        if (request.Settings is null)
        {
            return new SaveBulkPrintProfileResult(null, "The profile has no settings.");
        }

        var json = JsonSerializer.Serialize(request.Settings, JsonOptions);

        if (json.Length > MaxSettingsLength)
        {
            return new SaveBulkPrintProfileResult(null, "The profile is too large.");
        }

        var profiles = await Own(templateId, ownerUserId).ToListAsync(cancellationToken);
        var profile = profiles.FirstOrDefault(existing => string.Equals(existing.Name, name, StringComparison.CurrentCultureIgnoreCase));

        if (profile is null)
        {
            if (profiles.Count >= MaxPerTemplate)
            {
                return new SaveBulkPrintProfileResult(null, $"A template can have at most {MaxPerTemplate} profiles. Delete one first.");
            }

            profile = new BulkPrintProfile { TemplateId = templateId, OwnerUserId = ownerUserId, Name = name, SettingsJson = json };
            db.BulkPrintProfiles.Add(profile);
        }
        else if (!request.Replace)
        {
            return new SaveBulkPrintProfileResult(null, $"A profile named \"{profile.Name}\" already exists.", NameTaken: true);
        }
        else
        {
            profile.Name = name;
            profile.SettingsJson = json;
            profile.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SaveBulkPrintProfileResult(ToResponse(profile), null);
    }

    /// <returns>False when the profile doesn't exist or belongs to another account.</returns>
    public async Task<bool> DeleteAsync(int id, int? ownerUserId, CancellationToken cancellationToken)
    {
        var profile = await db.BulkPrintProfiles
            .SingleOrDefaultAsync(existing => existing.Id == id && existing.OwnerUserId == ownerUserId, cancellationToken);

        if (profile is null)
        {
            return false;
        }

        db.BulkPrintProfiles.Remove(profile);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private IQueryable<BulkPrintProfile> Own(int templateId, int? ownerUserId) =>
        db.BulkPrintProfiles.Where(profile => profile.TemplateId == templateId && profile.OwnerUserId == ownerUserId);

    private static BulkPrintProfileResponse ToResponse(BulkPrintProfile profile)
    {
        BulkPrintProfileSettings? settings = null;
        try
        {
            settings = JsonSerializer.Deserialize<BulkPrintProfileSettings>(profile.SettingsJson, JsonOptions);
        }
        catch (JsonException)
        {
            // An unreadable profile still shows up, so it can be deleted or saved again.
        }

        return new BulkPrintProfileResponse(
            profile.Id, profile.TemplateId, profile.Name,
            settings ?? new BulkPrintProfileSettings(null, null, null, null, false, [], null, null, null, null, null, null),
            profile.UpdatedAt);
    }
}
