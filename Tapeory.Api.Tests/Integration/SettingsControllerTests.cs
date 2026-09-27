using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tapeory.Api.Settings;

namespace Tapeory.Api.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class SettingsControllerTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Update_PersistsValues_AndGetReturnsThem()
    {
        var response = await _client.PutAsJsonAsync("/api/settings", new { language = "de", theme = "dark", unit = "inch" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await _client.GetFromJsonAsync<AppSettingsResponse>("/api/settings", JsonOptions);
        Assert.Equal(new AppSettingsResponse("de", "dark", "inch"), stored);
    }

    [Fact]
    public async Task Update_IsPartial_LeavingOmittedValuesAlone()
    {
        await _client.PutAsJsonAsync("/api/settings", new { language = "it", theme = "light", unit = "mm" });

        var response = await _client.PutAsJsonAsync("/api/settings", new { unit = "inch" });
        var updated = await response.Content.ReadFromJsonAsync<AppSettingsResponse>(JsonOptions);

        Assert.Equal(new AppSettingsResponse("it", "light", "inch"), updated);
    }

    [Theory]
    [InlineData("""{"unit":"cm"}""")]
    [InlineData("""{"theme":"blue"}""")]
    [InlineData("""{"language":"sw"}""")]
    public async Task Update_RejectsUnknownValues(string body)
    {
        var response = await _client.PutAsync("/api/settings", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("es")]
    public async Task Update_AcceptsEverySupportedLanguage(string language)
    {
        var response = await _client.PutAsJsonAsync("/api/settings", new { language });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AppSettingsResponse>(JsonOptions);
        Assert.Equal(language, updated!.Language);
    }
}

[Collection(IntegrationTestCollection.Name)]
public sealed class FontsControllerTests(TapeoryWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task List_ReturnsInstalledFamilies_AndEachCanBeDownloaded()
    {
        var families = await _client.GetFromJsonAsync<string[]>("/api/fonts");
        Assert.NotNull(families);

        foreach (var family in families.Take(2))
        {
            var response = await _client.GetAsync($"/api/fonts/file?family={Uri.EscapeDataString(family)}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.StartsWith("font/", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task File_Returns404_ForAFontThatIsNotInstalled()
    {
        var response = await _client.GetAsync("/api/fonts/file?family=Definitely%20Not%20Installed%207f3a");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
