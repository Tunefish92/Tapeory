using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tapeory.Api.PrintData;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class BulkPrintProfilesTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<int> CreateTemplateAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/templates",
            new CreateTemplateRequest(
                $"Profiles-{Guid.NewGuid():N}", null, null, null, 40m, 12m,
                """{"formatVersion":1,"widthMm":40,"heightMm":12,"objects":[]}""",
                [new TemplateFieldDto("name", "Name", null, true)]),
            JsonOptions);
        return (await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions))!.Id;
    }

    private static BulkPrintProfileSettings Settings(string file = "names.csv", int column = 0) => new(
        file, $"/home/ada/{file}", ";", null, true, [new BulkPrintProfileColumn("name", column, "Name")], 1, "Copies", null, "Front desk", "Standard", "AutoCut", 3);

    private async Task<BulkPrintProfileResponse> SaveAsync(int templateId, string name, BulkPrintProfileSettings settings)
    {
        var response = await _client.PutAsJsonAsync($"/api/templates/{templateId}/bulk-print-profiles", new SaveBulkPrintProfileRequest(name, settings), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BulkPrintProfileResponse>(JsonOptions))!;
    }

    private Task<List<BulkPrintProfileResponse>?> ListAsync(int templateId) =>
        _client.GetFromJsonAsync<List<BulkPrintProfileResponse>>($"/api/templates/{templateId}/bulk-print-profiles", JsonOptions);

    [Fact]
    public async Task AProfile_IsSavedWithEverythingItNeeds_AndListedForItsTemplate()
    {
        var template = await CreateTemplateAsync();
        var other = await CreateTemplateAsync();

        var saved = await SaveAsync(template, "  Weekly stock  ", Settings());

        var listed = Assert.Single((await ListAsync(template))!);
        Assert.Equal((saved.Id, "Weekly stock", template), (listed.Id, listed.Name, listed.TemplateId));
        Assert.Equal("/home/ada/names.csv", listed.Settings.FilePath);
        Assert.Equal((";", true, 1, "Copies"), (listed.Settings.Separator, listed.Settings.HasHeader, listed.Settings.QuantityColumn, listed.Settings.QuantityHeader));
        Assert.Equal(new BulkPrintProfileColumn("name", 0, "Name"), Assert.Single(listed.Settings.Columns!));
        Assert.Equal(("Front desk", "Standard", "AutoCut", 3), (listed.Settings.PrinterName, listed.Settings.Quality, listed.Settings.CutMode, listed.Settings.Copies));
        Assert.Empty((await ListAsync(other))!);
    }

    [Fact]
    public async Task SavingUnderAnExistingName_IsRefusedUntilReplacingIsAskedFor()
    {
        var template = await CreateTemplateAsync();
        var path = $"/api/templates/{template}/bulk-print-profiles";
        var first = await SaveAsync(template, "Stock", Settings());
        await SaveAsync(template, "Another", Settings());

        var refused = await _client.PutAsJsonAsync(path, new SaveBulkPrintProfileRequest(" stock ", Settings("new.xlsx", column: 3)), JsonOptions);
        var unchanged = (await ListAsync(template))!;
        var replaced = await _client.PutAsJsonAsync(path, new SaveBulkPrintProfileRequest("stock", Settings("new.xlsx", column: 3), Replace: true), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("already exists", await refused.Content.ReadAsStringAsync());
        Assert.Equal("names.csv", unchanged.Single(profile => profile.Name == "Stock").Settings.FileName);

        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var listed = (await ListAsync(template))!;
        Assert.Equal(first.Id, (await replaced.Content.ReadFromJsonAsync<BulkPrintProfileResponse>(JsonOptions))!.Id);
        Assert.Equal(["Another", "stock"], listed.Select(profile => profile.Name));
        Assert.Equal(("new.xlsx", 3), (listed[1].Settings.FileName, listed[1].Settings.Columns![0].Column));
    }

    [Fact]
    public async Task AProfile_CanBeDeleted()
    {
        var template = await CreateTemplateAsync();
        var saved = await SaveAsync(template, "Stock", Settings());

        var deleted = await _client.DeleteAsync($"/api/bulk-print-profiles/{saved.Id}");
        var again = await _client.DeleteAsync($"/api/bulk-print-profiles/{saved.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Empty((await ListAsync(template))!);
    }

    [Fact]
    public async Task AProfile_NeedsANameSettingsAndAnExistingTemplate()
    {
        var template = await CreateTemplateAsync();
        var path = $"/api/templates/{template}/bulk-print-profiles";

        var noName = await _client.PutAsJsonAsync(path, new SaveBulkPrintProfileRequest("  ", Settings()), JsonOptions);
        var longName = await _client.PutAsJsonAsync(path, new SaveBulkPrintProfileRequest(new string('x', 101), Settings()), JsonOptions);
        var noSettings = await _client.PutAsJsonAsync(path, new SaveBulkPrintProfileRequest("Stock", null), JsonOptions);
        var unknown = await _client.PutAsJsonAsync($"/api/templates/{int.MaxValue}/bulk-print-profiles", new SaveBulkPrintProfileRequest("Stock", Settings()), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Contains("name", await noName.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, longName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noSettings.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/templates/{int.MaxValue}/bulk-print-profiles")).StatusCode);
    }

    [Fact]
    public async Task ADeletedTemplatesProfiles_AreOutOfReach_ButCanStillBeDeleted()
    {
        var template = await CreateTemplateAsync();
        var saved = await SaveAsync(template, "Stock", Settings());

        await _client.DeleteAsync($"/api/templates/{template}");

        // The template is gone for the user, and so is the way to its profiles.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/templates/{template}/bulk-print-profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/bulk-print-profiles/{saved.Id}")).StatusCode);
    }
}
