using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printers;
using Tapeory.Api.PrintJobs;
using Tapeory.Api.Stats;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class PrintJobsControllerTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private async Task<TemplateDetailResponse> CreateTemplateWithFieldAsync(
        bool required = true, string? defaultValue = null)
    {
        var editorJson = """
            {"formatVersion":1,"widthMm":40,"heightMm":20,"objects":[
                {"type":"dynamicField","id":"f1","x":2,"y":2,"rotation":0,"locked":false,"hidden":false,
                 "fieldName":"name","label":"Name","defaultValue":"","required":true,
                 "width":36,"height":10,"fontSize":10,"fontFamily":"Arial","fontWeight":"normal",
                 "align":"left","fill":"#000000"}
            ]}
            """;

        var request = new CreateTemplateRequest(
            UniqueName("PrintTest"),
            null, null, null,
            40m, 20m,
            editorJson,
            [new TemplateFieldDto("name", "Name", defaultValue, required)]);

        var response = await _client.PostAsJsonAsync("/api/templates", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);
        Assert.NotNull(created);
        return created!;
    }

    [Fact]
    public async Task CreatePrintJob_ReturnsCreated_WhenTheRequiredFieldIsProvided()
    {
        var template = await CreateTemplateWithFieldAsync();

        var request = new CreatePrintJobRequest(
            template.Id, null, "Test Printer",
            [new PrintJobItemRequest(new Dictionary<string, string> { ["name"] = "Alice" }, 1)]);

        var response = await _client.PostAsJsonAsync("/api/print-jobs", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var job = await response.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(template.Id, job!.TemplateId);
        Assert.Equal("Test Printer", job.PrinterName);
        Assert.Single(job.Items);
        Assert.Equal("Alice", job.Items[0].FieldValues["name"]);
    }

    [Fact]
    public async Task CreatePrintJob_ReturnsNotFound_ForAnUnknownTemplate()
    {
        var request = new CreatePrintJobRequest(
            999999999, null, null, [new PrintJobItemRequest(new Dictionary<string, string>(), 1)]);

        var response = await _client.PostAsJsonAsync("/api/print-jobs", request, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreatePrintJob_ReturnsBadRequest_WhenNoItemsAreProvided()
    {
        var template = await CreateTemplateWithFieldAsync(required: false);

        var request = new CreatePrintJobRequest(template.Id, null, null, []);

        var response = await _client.PostAsJsonAsync("/api/print-jobs", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePrintJob_ReturnsBadRequest_WhenARequiredFieldHasNoValueAndNoDefault()
    {
        var template = await CreateTemplateWithFieldAsync(required: true, defaultValue: null);

        var request = new CreatePrintJobRequest(
            template.Id, null, null, [new PrintJobItemRequest(new Dictionary<string, string>(), 1)]);

        var response = await _client.PostAsJsonAsync("/api/print-jobs", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePrintJob_Succeeds_WhenARequiredFieldHasADefaultAndNoneIsSubmitted()
    {
        var template = await CreateTemplateWithFieldAsync(required: true, defaultValue: "Fallback");

        var request = new CreatePrintJobRequest(
            template.Id, null, null, [new PrintJobItemRequest(new Dictionary<string, string>(), 1)]);

        var response = await _client.PostAsJsonAsync("/api/print-jobs", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreatePrintJob_ReturnsBadRequest_WhenQuantityIsNotPositive()
    {
        var template = await CreateTemplateWithFieldAsync(required: false);

        var request = new CreatePrintJobRequest(
            template.Id, null, null,
            [new PrintJobItemRequest(new Dictionary<string, string>(), 0)]);

        var response = await _client.PostAsJsonAsync("/api/print-jobs", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPrintJob_ReturnsNotFound_ForAnUnknownId()
    {
        var response = await _client.GetAsync("/api/print-jobs/999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListPrintJobs_FiltersByTemplateId()
    {
        var templateA = await CreateTemplateWithFieldAsync(required: false);
        var templateB = await CreateTemplateWithFieldAsync(required: false);

        foreach (var template in new[] { templateA, templateB })
        {
            var request = new CreatePrintJobRequest(
                template.Id, null, null, [new PrintJobItemRequest(new Dictionary<string, string>(), 1)]);
            await _client.PostAsJsonAsync("/api/print-jobs", request, JsonOptions);
        }

        var response = await _client.GetAsync($"/api/print-jobs?templateId={templateA.Id}");
        var jobs = await response.Content.ReadFromJsonAsync<PrintJobResponse[]>(JsonOptions);

        Assert.NotNull(jobs);
        Assert.NotEmpty(jobs!);
        Assert.All(jobs!, job => Assert.Equal(templateA.Id, job.TemplateId));
    }

    [Fact]
    public async Task EndToEnd_TheBackgroundProcessorCompletesTheJob_AndTheRenderedPreviewIsDownloadable()
    {
        var template = await CreateTemplateWithFieldAsync();

        var createResponse = await _client.PostAsJsonAsync(
            "/api/print-jobs",
            new CreatePrintJobRequest(
                template.Id, null, null,
                [new PrintJobItemRequest(new Dictionary<string, string> { ["name"] = "Bob" }, 1)]),
            JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions);

        var completed = await WaitForTerminalStatusAsync(created!.Id, TimeSpan.FromSeconds(30));

        Assert.Equal("Completed", completed.Status);
        Assert.Equal("Completed", completed.Items[0].Status);
        Assert.NotNull(completed.Items[0].PreviewUrl);

        var previewResponse = await _client.GetAsync(completed.Items[0].PreviewUrl);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Equal("image/png", previewResponse.Content.Headers.ContentType?.MediaType);

        var bytes = await previewResponse.Content.ReadAsByteArrayAsync();
        using var bitmap = SkiaSharp.SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
    }

    [Fact]
    public async Task EndToEnd_AJobForATemplateWithMalformedEditorJson_ReachesFailed_RatherThanHangingInProcessing()
    {
        // Regression test: nothing validates editorJson is well-formed JSON at creation time, so
        // this is reachable in practice. The processor must still leave the job in a terminal
        // state instead of getting stuck "Processing" forever with no explanation.
        var request = new CreateTemplateRequest(
            UniqueName("Malformed"), null, null, null, 40m, 20m, "not valid json{{{", null);
        var createTemplateResponse = await _client.PostAsJsonAsync("/api/templates", request, JsonOptions);
        var template = await createTemplateResponse.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        var createJobResponse = await _client.PostAsJsonAsync(
            "/api/print-jobs",
            new CreatePrintJobRequest(
                template!.Id, null, null, [new PrintJobItemRequest(new Dictionary<string, string>(), 1)]),
            JsonOptions);
        var created = await createJobResponse.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions);

        var result = await WaitForTerminalStatusAsync(created!.Id, TimeSpan.FromSeconds(30));

        Assert.Equal("Failed", result.Status);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task EndToEnd_AJobWithAConfiguredPrinter_ActuallySendsTheRenderedLabelOverTheSocket()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();

        var printerResponse = await _client.PostAsJsonAsync(
            "/api/printers",
            new CreatePrinterRequest(
                UniqueName("Printer"), "Brother QL-800", "IpAddress", "127.0.0.1", port,
                null, null, 40m, 20m, true),
            JsonOptions);
        var printer = await printerResponse.Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions);

        var template = await CreateTemplateWithFieldAsync();

        var createJobResponse = await _client.PostAsJsonAsync(
            "/api/print-jobs",
            new CreatePrintJobRequest(
                template.Id, printer!.Id, null,
                [new PrintJobItemRequest(new Dictionary<string, string> { ["name"] = "Carol" }, 1)]),
            JsonOptions);
        var created = await createJobResponse.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions);

        Assert.Equal(printer.Id, created!.PrinterId);
        Assert.Equal(printer.Name, created.PrinterName);

        using var serverClient = await acceptTask;
        var buffer = new byte[8];
        var read = await serverClient.GetStream().ReadAsync(buffer);

        Assert.True(read > 0);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], buffer); // PNG signature

        var completed = await WaitForTerminalStatusAsync(created.Id, TimeSpan.FromSeconds(30));

        Assert.Equal("Completed", completed.Status);
        Assert.Equal("Completed", completed.Items[0].Status);
    }

    [Fact]
    public async Task GetItemPreview_ReturnsNotFound_ForAnUnknownItem()
    {
        var response = await _client.GetAsync("/api/print-jobs/items/999999999/preview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletePrintJob_RemovesItFromTheHistoryAndDisk_ButKeepsCountingItInTheStatistics()
    {
        var job = await CreateCompletedJobAsync("Dora", quantity: 3);
        var previewUrl = job.Items[0].PreviewUrl!;
        var renderedPath = await RenderedFilePathAsync(job.Items[0].Id);
        Assert.True(File.Exists(renderedPath));
        var labelsBefore = await LabelsPrintedAsync();

        var response = await _client.DeleteAsync($"/api/print-jobs/{job.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/print-jobs/{job.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(previewUrl)).StatusCode);
        var list = await _client.GetFromJsonAsync<List<PrintJobResponse>>("/api/print-jobs", JsonOptions);
        Assert.DoesNotContain(list!, listed => listed.Id == job.Id);
        Assert.False(File.Exists(renderedPath));

        // Kept only for the statistics: what was printed is gone, how much was printed isn't.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.PrintJobs.Include(j => j.Items).SingleAsync(j => j.Id == job.Id);
            Assert.NotNull(stored.DeletedAt);
            Assert.Equal("{}", stored.Items[0].FieldValuesJson);
            Assert.Null(stored.Items[0].RenderedImageFileId);
            Assert.Equal(3, stored.Items[0].Quantity);
        }

        // Other tests' jobs may complete meanwhile, so the count can only have grown, never shrunk.
        Assert.True(await LabelsPrintedAsync() >= labelsBefore);
    }

    [Fact]
    public async Task DeletePrintJob_ReturnsConflict_WhileTheJobIsStillPrinting()
    {
        var jobId = await InsertJobStillPrintingAsync();

        var response = await _client.DeleteAsync($"/api/print-jobs/{jobId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/print-jobs/{jobId}")).StatusCode);
    }

    [Fact]
    public async Task DeletePrintJob_ReturnsNotFound_ForAnUnknownOrAlreadyDeletedJob()
    {
        var job = await CreateCompletedJobAsync("Eve");
        await _client.DeleteAsync($"/api/print-jobs/{job.Id}");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/print-jobs/{job.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/print-jobs/999999999")).StatusCode);
    }

    [Fact]
    public async Task DeleteAllPrintJobs_ClearsFinishedJobs_AndLeavesJobsThatAreStillPrinting()
    {
        var finished = await CreateCompletedJobAsync("Frank");
        var stillPrintingId = await InsertJobStillPrintingAsync();

        var response = await _client.DeleteAsync("/api/print-jobs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DeleteAllPrintJobsResponse>(JsonOptions);
        Assert.True(result!.Deleted >= 1);
        Assert.True(result.SkippedInProgress >= 1);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/print-jobs/{finished.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/print-jobs/{stillPrintingId}")).StatusCode);
    }

    [Fact]
    public async Task DeletedTemplate_StaysInPrintHistory_ButCannotBePrintedAgain()
    {
        var job = await CreateCompletedJobAsync("Hank");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/templates/{job.TemplateId}")).StatusCode);

        var afterDelete = await _client.GetFromJsonAsync<PrintJobResponse>($"/api/print-jobs/{job.Id}", JsonOptions);
        Assert.True(afterDelete!.TemplateDeleted);
        Assert.Equal(job.TemplateName, afterDelete.TemplateName);

        var reprint = await _client.PostAsJsonAsync(
            "/api/print-jobs",
            new CreatePrintJobRequest(
                job.TemplateId, null, null,
                [new PrintJobItemRequest(new Dictionary<string, string> { ["name"] = "Hank" }, 1)]),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, reprint.StatusCode);
    }

    private async Task<PrintJobResponse> CreateCompletedJobAsync(string name, int quantity = 1)
    {
        var template = await CreateTemplateWithFieldAsync();
        var createResponse = await _client.PostAsJsonAsync(
            "/api/print-jobs",
            new CreatePrintJobRequest(
                template.Id, null, null,
                [new PrintJobItemRequest(new Dictionary<string, string> { ["name"] = name }, quantity)]),
            JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions);

        var completed = await WaitForTerminalStatusAsync(created!.Id, TimeSpan.FromSeconds(30));
        Assert.Equal("Completed", completed.Status);
        return completed;
    }

    /// <summary>A job the processor has already claimed. Inserted directly because a real one
    /// finishes too quickly to catch mid-print; the processor only picks up Queued jobs, so it
    /// stays Processing.</summary>
    private async Task<int> InsertJobStillPrintingAsync()
    {
        var template = await CreateTemplateWithFieldAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new PrintJob
        {
            TemplateId = template.Id,
            TemplateVersionNumber = 1,
            Status = PrintJobStatus.Processing,
            Items = [new PrintJobItem { FieldValuesJson = """{"name":"Gina"}""", Status = PrintJobStatus.Processing }]
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<string> RenderedFilePathAsync(int itemId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.PrintJobItems.Include(i => i.RenderedImageFile).SingleAsync(i => i.Id == itemId);
        var root = scope.ServiceProvider.GetRequiredService<StorageService>().RootPath;
        return Path.Combine(root, item.RenderedImageFile!.RelativePath);
    }

    private async Task<int> LabelsPrintedAsync()
    {
        var stats = await _client.GetFromJsonAsync<DashboardStatsResponse>("/api/stats", JsonOptions);
        return stats!.LabelsPrinted;
    }

    private async Task<PrintJobResponse> WaitForTerminalStatusAsync(int jobId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var response = await _client.GetAsync($"/api/print-jobs/{jobId}");
            var job = await response.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions);

            if (job!.Status is "Completed" or "Failed")
            {
                return job;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Print job {jobId} did not reach a terminal status within {timeout}.");
    }
}
