using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tapeory.Api.PrintJobs;
using Tapeory.Api.Stats;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class StatsControllerTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<DashboardStatsResponse> GetStatsAsync()
    {
        var response = await _client.GetAsync("/api/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DashboardStatsResponse>(JsonOptions))!;
    }

    // The database is shared across the integration collection, so assertions compare the
    // before/after delta rather than absolute totals.
    [Fact]
    public async Task Stats_CountPrintedLabelsAndTapeLength_OfCompletedJobs()
    {
        var before = await GetStatsAsync();

        var templateResponse = await _client.PostAsJsonAsync(
            "/api/templates",
            new CreateTemplateRequest(
                $"Stats-{Guid.NewGuid():N}", null, null, null, 40m, 20m,
                """{"formatVersion":1,"widthMm":40,"heightMm":20,"objects":[]}""", null),
            JsonOptions);
        var template = await templateResponse.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        var jobResponse = await _client.PostAsJsonAsync(
            "/api/print-jobs",
            new CreatePrintJobRequest(template!.Id, null, null, [new PrintJobItemRequest([], 3)]),
            JsonOptions);
        var job = await jobResponse.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions);
        await WaitForCompletionAsync(job!.Id);

        var after = await GetStatsAsync();

        Assert.Equal(before.TemplateCount + 1, after.TemplateCount);
        Assert.Equal(before.PrintJobCount + 1, after.PrintJobCount);
        Assert.Equal(before.LabelsPrinted + 3, after.LabelsPrinted);
        Assert.Equal(before.TotalPrintedLengthMm + 120m, after.TotalPrintedLengthMm);
        Assert.Equal(before.TotalPrintedAreaMm2 + 2400m, after.TotalPrintedAreaMm2);
        Assert.NotNull(after.LastPrintedAt);
    }

    [Fact]
    public async Task Reset_StartsPrintStatsFromZero_KeepsHistory_AndCanBeUndone()
    {
        try
        {
            var template = await CreateTemplateAsync();
            var oldJobId = await PrintAsync(template.Id, quantity: 3);

            var resetResponse = await _client.PostAsync("/api/stats/reset", null);
            Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
            var reset = (await resetResponse.Content.ReadFromJsonAsync<DashboardStatsResponse>(JsonOptions))!;

            Assert.NotNull(reset.StatsSince);
            Assert.Equal(0, reset.PrintJobCount);
            Assert.Equal(0, reset.LabelsPrinted);
            Assert.Equal(0m, reset.TotalPrintedLengthMm);
            Assert.Null(reset.MostPrintedTemplateName);
            Assert.Null(reset.LastPrintedAt);
            // Inventory isn't a statistic: templates still exist after a reset.
            Assert.True(reset.TemplateCount >= 1);

            // Print history survives.
            var oldJob = await _client.GetAsync($"/api/print-jobs/{oldJobId}");
            Assert.Equal(HttpStatusCode.OK, oldJob.StatusCode);

            await PrintAsync(template.Id, quantity: 2);
            var afterNewJob = await GetStatsAsync();
            Assert.Equal(1, afterNewJob.PrintJobCount);
            Assert.Equal(2, afterNewJob.LabelsPrinted);
            Assert.Equal(80m, afterNewJob.TotalPrintedLengthMm);

            var restoreResponse = await _client.DeleteAsync("/api/stats/reset");
            Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
            var restored = (await restoreResponse.Content.ReadFromJsonAsync<DashboardStatsResponse>(JsonOptions))!;

            Assert.Null(restored.StatsSince);
            Assert.True(restored.LabelsPrinted >= 5);
        }
        finally
        {
            // Other tests in the shared database expect all-time stats.
            await _client.DeleteAsync("/api/stats/reset");
        }
    }

    [Fact]
    public async Task Reset_Twice_MovesTheStartForward()
    {
        try
        {
            var first = await (await _client.PostAsync("/api/stats/reset", null))
                .Content.ReadFromJsonAsync<DashboardStatsResponse>(JsonOptions);
            await Task.Delay(20);
            var second = await (await _client.PostAsync("/api/stats/reset", null))
                .Content.ReadFromJsonAsync<DashboardStatsResponse>(JsonOptions);

            Assert.True(second!.StatsSince > first!.StatsSince);
        }
        finally
        {
            await _client.DeleteAsync("/api/stats/reset");
        }
    }

    [Fact]
    public async Task RestoreAllTime_WithoutAReset_IsHarmless()
    {
        var response = await _client.DeleteAsync("/api/stats/reset");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stats = await response.Content.ReadFromJsonAsync<DashboardStatsResponse>(JsonOptions);
        Assert.Null(stats!.StatsSince);
    }

    private async Task<TemplateDetailResponse> CreateTemplateAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/templates",
            new CreateTemplateRequest(
                $"Stats-{Guid.NewGuid():N}", null, null, null, 40m, 20m,
                """{"formatVersion":1,"widthMm":40,"heightMm":20,"objects":[]}""", null),
            JsonOptions);
        return (await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions))!;
    }

    private async Task<int> PrintAsync(int templateId, int quantity)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/print-jobs",
            new CreatePrintJobRequest(templateId, null, null, [new PrintJobItemRequest([], quantity)]),
            JsonOptions);
        var job = await response.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions);
        await WaitForCompletionAsync(job!.Id);
        return job.Id;
    }

    private async Task WaitForCompletionAsync(int jobId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            var job = await _client.GetFromJsonAsync<PrintJobResponse>($"/api/print-jobs/{jobId}", JsonOptions);

            if (job!.Status == "Completed")
            {
                return;
            }

            Assert.NotEqual("Failed", job.Status);
            await Task.Delay(200);
        }

        throw new TimeoutException($"Print job {jobId} did not complete in time.");
    }
}
