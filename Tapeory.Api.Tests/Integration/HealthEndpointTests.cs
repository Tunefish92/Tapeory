using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tapeory.Api.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class HealthEndpointTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_ReturnsOk_WithConnectedDatabaseAndStoragePath()
    {
        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("ok", payload!.Status);
        Assert.True(payload.DatabaseConnected);
        Assert.False(string.IsNullOrWhiteSpace(payload.StoragePath));
    }

    private sealed record HealthResponse(string Status, string StoragePath, bool DatabaseConnected);
}
