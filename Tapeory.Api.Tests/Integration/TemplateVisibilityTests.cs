using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tapeory.Api.Auth;
using Tapeory.Api.Backups;
using Tapeory.Api.Data;
using Tapeory.Api.PrintJobs;
using Tapeory.Api.Stats;
using Tapeory.Api.Templates;
using Tapeory.Api.Uploads;

namespace Tapeory.Api.Tests.Integration;

/// <summary>
/// Private and public templates. Like <see cref="AuthTests"/>, each test removes the accounts it
/// created, so the rest of the integration tests keep seeing an open install.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class TemplateVisibilityTests(TapeoryWebApplicationFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string Document = """{"formatVersion":1,"widthMm":40,"heightMm":20,"objects":[]}""";

    public Task InitializeAsync() => RemoveAccountsAsync();

    public Task DisposeAsync() => RemoveAccountsAsync();

    private async Task RemoveAccountsAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.ExecuteDeleteAsync();
        factory.Services.GetRequiredService<UserDirectory>().Reset();
    }

    private HttpClient NewClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthSetup.CsrfHeader, "Tapeory");
        return client;
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task<HttpClient> CreateAdminAsync()
    {
        var admin = NewClient();
        var response = await admin.PostAsJsonAsync("/api/auth/first-user", new FirstUserRequest(Unique("admin"), "The Admin", "admin password"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return admin;
    }

    private async Task<HttpClient> CreateUserAsync(HttpClient admin, string displayName)
    {
        var created = await (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(Unique("user"), displayName, "User")))
            .Content.ReadFromJsonAsync<TemporaryPasswordResponse>(JsonOptions);
        var client = NewClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(created!.User.UserName, created.TemporaryPassword, false));
        var changed = await client.PutAsJsonAsync("/api/auth/password", new ChangePasswordRequest(created.TemporaryPassword, "users password"));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        return client;
    }

    private static async Task<TemplateDetailResponse> CreateTemplateAsync(HttpClient client, string? group = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/templates",
            new CreateTemplateRequest(Unique("Tpl"), null, group, null, 40m, 20m, Document, null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions))!;
    }

    private static Task<HttpResponseMessage> SetPublicAsync(HttpClient client, int id, bool isPublic) =>
        client.PutAsJsonAsync($"/api/templates/{id}/visibility", new SetVisibilityRequest(isPublic));

    private static Task<HttpResponseMessage> PrintAsync(HttpClient client, int templateId) =>
        client.PostAsJsonAsync(
            "/api/print-jobs",
            new CreatePrintJobRequest(templateId, null, null, [new PrintJobItemRequest([], 1)]),
            JsonOptions);

    private static async Task<List<TemplateSummaryResponse>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<TemplateSummaryResponse>>("/api/templates", JsonOptions))!;

    [Fact]
    public async Task NewTemplates_ArePrivate_ToEveryoneButTheOwnerAndAdmins()
    {
        var admin = await CreateAdminAsync();
        var ada = await CreateUserAsync(admin, "Ada");
        var grace = await CreateUserAsync(admin, "Grace");

        var template = await CreateTemplateAsync(ada);
        Assert.False(template.IsPublic);
        Assert.Equal("Ada", template.OwnerName);
        Assert.True(template.CanEdit);

        // To Grace it doesn't exist.
        Assert.DoesNotContain(await ListAsync(grace), t => t.Id == template.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await grace.GetAsync($"/api/templates/{template.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grace.GetAsync($"/api/templates/{template.Id}/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grace.GetAsync($"/api/templates/{template.Id}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grace.GetAsync($"/api/templates/{template.Id}/thumbnail")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await grace.PostAsJsonAsync($"/api/templates/{template.Id}/preview", new PreviewRequest(null, "png"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grace.PostAsJsonAsync($"/api/templates/{template.Id}/duplicate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grace.DeleteAsync($"/api/templates/{template.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PrintAsync(grace, template.Id)).StatusCode);

        // Admins see and change everything.
        Assert.Contains(await ListAsync(admin), t => t.Id == template.Id);
        var asAdmin = await admin.GetFromJsonAsync<TemplateDetailResponse>($"/api/templates/{template.Id}", JsonOptions);
        Assert.True(asAdmin!.CanEdit);
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PutAsJsonAsync(
                $"/api/templates/{template.Id}",
                new UpdateTemplateMetadataRequest("Renamed by admin", null, null, null, null))).StatusCode);
    }

    [Fact]
    public async Task PublicTemplates_CanBeSeenPrintedAndDuplicated_ButOnlyChangedByTheOwnerOrAdmins()
    {
        var admin = await CreateAdminAsync();
        var ada = await CreateUserAsync(admin, "Ada");
        var grace = await CreateUserAsync(admin, "Grace");
        var template = await CreateTemplateAsync(ada);

        Assert.Equal(HttpStatusCode.OK, (await SetPublicAsync(ada, template.Id, true)).StatusCode);

        var seen = await grace.GetFromJsonAsync<TemplateDetailResponse>($"/api/templates/{template.Id}", JsonOptions);
        Assert.True(seen!.IsPublic);
        Assert.False(seen.CanEdit);
        Assert.Equal("Ada", seen.OwnerName);
        Assert.Contains(await ListAsync(grace), t => t.Id == template.Id && !t.CanEdit);
        Assert.Equal(HttpStatusCode.Created, (await PrintAsync(grace, template.Id)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await grace.PutAsJsonAsync(
                $"/api/templates/{template.Id}",
                new UpdateTemplateMetadataRequest("Mine now", null, null, null, null))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await grace.PostAsJsonAsync(
                $"/api/templates/{template.Id}/versions",
                new CreateTemplateVersionRequest(40m, 20m, Document, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await grace.DeleteAsync($"/api/templates/{template.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetPublicAsync(grace, template.Id, false)).StatusCode);

        // A duplicate is Grace's own, private copy.
        var copyResponse = await grace.PostAsJsonAsync($"/api/templates/{template.Id}/duplicate", new { });
        Assert.Equal(HttpStatusCode.Created, copyResponse.StatusCode);
        var copy = await copyResponse.Content.ReadFromJsonAsync<TemplateSummaryResponse>(JsonOptions);
        Assert.False(copy!.IsPublic);
        Assert.Equal("Grace", copy.OwnerName);
        Assert.True(copy.CanEdit);
        Assert.Equal(HttpStatusCode.NotFound, (await ada.GetAsync($"/api/templates/{copy.Id}")).StatusCode);

        // Made private again, it disappears for Grace.
        Assert.Equal(HttpStatusCode.OK, (await SetPublicAsync(admin, template.Id, false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grace.GetAsync($"/api/templates/{template.Id}")).StatusCode);
    }

    [Fact]
    public async Task RenamingAGroup_OnlyMovesTemplatesYouMayChange()
    {
        var admin = await CreateAdminAsync();
        var ada = await CreateUserAsync(admin, "Ada");
        var grace = await CreateUserAsync(admin, "Grace");
        var group = Unique("Group");
        var adas = await CreateTemplateAsync(ada, group);
        await SetPublicAsync(ada, adas.Id, true);
        var graces = await CreateTemplateAsync(grace, group);

        var renamed = await grace.PostAsJsonAsync("/api/templates/groups/rename", new { from = group, to = "Graces" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var adasNow = await ada.GetFromJsonAsync<TemplateDetailResponse>($"/api/templates/{adas.Id}", JsonOptions);
        var gracesNow = await grace.GetFromJsonAsync<TemplateDetailResponse>($"/api/templates/{graces.Id}", JsonOptions);
        Assert.Equal(group, adasNow!.Category);
        Assert.Equal("Graces", gracesNow!.Category);
    }

    [Fact]
    public async Task TemplatesFromBeforeAccounts_AreShared_AndOnlyAdminsChangeThem()
    {
        // Created while the install is still open: no owner, public.
        var shared = await CreateTemplateAsync(NewClient());
        Assert.True(shared.IsPublic);
        Assert.Null(shared.OwnerName);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await SetPublicAsync(NewClient(), shared.Id, false)).StatusCode);

        var admin = await CreateAdminAsync();
        var ada = await CreateUserAsync(admin, "Ada");

        var seen = await ada.GetFromJsonAsync<TemplateDetailResponse>($"/api/templates/{shared.Id}", JsonOptions);
        Assert.False(seen!.CanEdit);
        Assert.Equal(HttpStatusCode.Forbidden, (await ada.DeleteAsync($"/api/templates/{shared.Id}")).StatusCode);

        // An admin making it private takes it over.
        var madePrivate = await (await SetPublicAsync(admin, shared.Id, false)).Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);
        Assert.Equal("The Admin", madePrivate!.OwnerName);
        Assert.Equal(HttpStatusCode.NotFound, (await ada.GetAsync($"/api/templates/{shared.Id}")).StatusCode);
    }

    private async Task<PrintJobResponse> PrintAndWaitAsync(HttpClient client, int templateId)
    {
        var response = await PrintAsync(client, templateId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions))!;

        for (var attempt = 0; attempt < 100 && job.Status is "Queued" or "Processing"; attempt++)
        {
            await Task.Delay(100);
            job = (await client.GetFromJsonAsync<PrintJobResponse>($"/api/print-jobs/{job.Id}", JsonOptions))!;
        }

        return job;
    }

    [Fact]
    public async Task PrintHistory_ShowsUsersOnlyTheirOwnJobs_AndAdminsEverything()
    {
        var admin = await CreateAdminAsync();
        var ada = await CreateUserAsync(admin, "Ada");
        var grace = await CreateUserAsync(admin, "Grace");
        var template = await CreateTemplateAsync(ada);
        await SetPublicAsync(ada, template.Id, true);

        var adasJob = await PrintAndWaitAsync(ada, template.Id);
        var gracesJob = await PrintAndWaitAsync(grace, template.Id);

        var adasHistory = await ada.GetFromJsonAsync<List<PrintJobResponse>>("/api/print-jobs", JsonOptions);
        Assert.Contains(adasHistory!, job => job.Id == adasJob.Id);
        Assert.DoesNotContain(adasHistory!, job => job.Id == gracesJob.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await ada.GetAsync($"/api/print-jobs/{gracesJob.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ada.GetAsync($"/api/print-jobs/items/{gracesJob.Items[0].Id}/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ada.DeleteAsync($"/api/print-jobs/{gracesJob.Id}")).StatusCode);

        // "Delete all" only clears your own history.
        Assert.Equal(HttpStatusCode.OK, (await ada.DeleteAsync("/api/print-jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await grace.GetAsync($"/api/print-jobs/{gracesJob.Id}")).StatusCode);

        var adminHistory = await admin.GetFromJsonAsync<List<PrintJobResponse>>("/api/print-jobs", JsonOptions);
        Assert.Contains(adminHistory!, job => job.Id == gracesJob.Id);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/print-jobs/items/{gracesJob.Items[0].Id}/preview")).StatusCode);
    }

    [Fact]
    public async Task Statistics_DontNameOtherPeoplesPrivateTemplates()
    {
        var admin = await CreateAdminAsync();
        var ada = await CreateUserAsync(admin, "Ada");
        var grace = await CreateUserAsync(admin, "Grace");
        var secret = await CreateTemplateAsync(ada);
        for (var i = 0; i < 3; i++)
        {
            await PrintAndWaitAsync(ada, secret.Id);
        }

        var gracesStats = await grace.GetFromJsonAsync<DashboardStatsResponse>("/api/stats", JsonOptions);
        var adasStats = await ada.GetFromJsonAsync<DashboardStatsResponse>("/api/stats", JsonOptions);

        Assert.NotEqual(secret.Name, gracesStats!.MostPrintedTemplateName);
        Assert.Equal(secret.Name, adasStats!.MostPrintedTemplateName);
        Assert.True(adasStats.TemplateCount > gracesStats.TemplateCount);
    }

    [Fact]
    public async Task DeletingAnAccount_TransfersItsTemplates_OrDeletesThem()
    {
        var admin = await CreateAdminAsync();
        var users = new List<(HttpClient Client, int Id)>();

        foreach (var name in new[] { "Ada", "Grace" })
        {
            var client = await CreateUserAsync(admin, name);
            var state = await client.GetFromJsonAsync<AuthStateResponse>("/api/auth/state", JsonOptions);
            users.Add((client, state!.User!.Id));
        }

        var adas = await CreateTemplateAsync(users[0].Client);
        var graces = await CreateTemplateAsync(users[1].Client);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{users[0].Id}")).StatusCode);
        var transferred = await admin.GetFromJsonAsync<TemplateDetailResponse>($"/api/templates/{adas.Id}", JsonOptions);
        Assert.Equal("The Admin", transferred!.OwnerName);
        Assert.False(transferred.IsPublic);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/users/{users[1].Id}?templates=maybe")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{users[1].Id}?templates=delete")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/templates/{graces.Id}")).StatusCode);
    }

    [Fact]
    public async Task LabelBackups_KeepOwnersAndVisibility()
    {
        var admin = await CreateAdminAsync();
        var ada = await CreateUserAsync(admin, "Ada");
        var privateOne = await CreateTemplateAsync(ada);
        var publicOne = await CreateTemplateAsync(ada);
        await SetPublicAsync(ada, publicOne.Id, true);

        var backup = await (await admin.PostAsync("/api/backups/labels", null)).Content.ReadFromJsonAsync<BackupInfo>(JsonOptions);
        var restore = await admin.PostAsync($"/api/backups/labels/{backup!.FileName}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var adasTemplates = await ListAsync(ada);
        Assert.Contains(adasTemplates, t => t.Name == privateOne.Name && !t.IsPublic && t.OwnerName == "Ada" && t.CanEdit);
        Assert.Contains(adasTemplates, t => t.Name == publicOne.Name && t.IsPublic && t.OwnerName == "Ada");
    }

    [Fact]
    public async Task Images_AreOnlyServed_ToWhoeverCanSeeATemplateUsingThem()
    {
        var admin = await CreateAdminAsync();
        var ada = await CreateUserAsync(admin, "Ada");
        var grace = await CreateUserAsync(admin, "Grace");

        var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent("fake png"u8.ToArray());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        upload.Add(file, "file", "logo.png");
        var image = await (await ada.PostAsync("/api/uploads/images", upload)).Content.ReadFromJsonAsync<UploadedImageResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, (await ada.GetAsync(image!.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grace.GetAsync(image.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(image.Url)).StatusCode);

        var design = $$"""{"formatVersion":1,"widthMm":40,"heightMm":20,"objects":[{"id":"i","type":"image","x":0,"y":0,"rotation":0,"locked":false,"hidden":false,"uploadedFileId":{{image.Id}},"url":"{{image.Url}}","width":10,"height":10}]}""";
        var created = await (await ada.PostAsJsonAsync(
                "/api/templates",
                new CreateTemplateRequest(Unique("Img"), null, null, null, 40m, 20m, design, null),
                JsonOptions))
            .Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        // Still private: Grace can't get the image. Once the template is public, she can.
        Assert.Equal(HttpStatusCode.NotFound, (await grace.GetAsync(image.Url)).StatusCode);
        await SetPublicAsync(ada, created!.Id, true);
        Assert.Equal(HttpStatusCode.OK, (await grace.GetAsync(image.Url)).StatusCode);
    }

    [Fact]
    public async Task BulkPrintProfiles_BelongToTheAccountThatSavedThem()
    {
        var admin = await CreateAdminAsync();
        var ada = await CreateUserAsync(admin, "Ada");
        var template = await CreateTemplateAsync(admin);
        await admin.PutAsJsonAsync($"/api/templates/{template.Id}/visibility", new SetVisibilityRequest(true), JsonOptions);
        var settings = new Tapeory.Api.PrintData.BulkPrintProfileSettings("a.csv", null, ",", null, true, [], null, null, null, null, null, null);
        var path = $"/api/templates/{template.Id}/bulk-print-profiles";

        var saved = await (await admin.PutAsJsonAsync(path, new Tapeory.Api.PrintData.SaveBulkPrintProfileRequest("Mine", settings), JsonOptions))
            .Content.ReadFromJsonAsync<Tapeory.Api.PrintData.BulkPrintProfileResponse>(JsonOptions);
        await ada.PutAsJsonAsync(path, new Tapeory.Api.PrintData.SaveBulkPrintProfileRequest("Mine", settings), JsonOptions);

        var adminsProfiles = await admin.GetFromJsonAsync<List<Tapeory.Api.PrintData.BulkPrintProfileResponse>>(path, JsonOptions);
        var adasProfiles = await ada.GetFromJsonAsync<List<Tapeory.Api.PrintData.BulkPrintProfileResponse>>(path, JsonOptions);
        var adaDeletesAdmins = await ada.DeleteAsync($"/api/bulk-print-profiles/{saved!.Id}");

        Assert.Equal(saved.Id, Assert.Single(adminsProfiles!).Id);
        Assert.NotEqual(saved.Id, Assert.Single(adasProfiles!).Id);
        Assert.Equal(HttpStatusCode.NotFound, adaDeletesAdmins.StatusCode);
    }
}
