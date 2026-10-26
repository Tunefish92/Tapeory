using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.PrintJobs;
using Tapeory.Api.Printers;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Tests.Integration;

/// <summary>Jobs with many rows: sent to the printer in batches, stopped after a failure or by
/// the user, and reprinted from where they stopped. The printer is the fake USB port.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class BulkPrintJobTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<PrinterResponse> CreateUsbPrinterAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/printers",
            new CreatePrinterRequest(
                $"Bulk-{Guid.NewGuid():N}", "PT-P750W", "Usb", null, null, null,
                TapeoryWebApplicationFactory.FakeUsbPort.Device, null, null, true),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions))!;
    }

    private async Task<TemplateDetailResponse> CreateTemplateAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/templates",
            new CreateTemplateRequest(
                $"Bulk-{Guid.NewGuid():N}", null, null, null, 40m, 12m,
                """
                {"formatVersion":1,"widthMm":40,"heightMm":12,"objects":[
                    {"type":"dynamicField","id":"f1","x":2,"y":2,"rotation":0,"fieldName":"name",
                     "width":36,"height":8,"fontSize":10,"fontFamily":"Inter","fontWeight":"normal","align":"left","fill":"#000000"}
                ]}
                """,
                [new TemplateFieldDto("name", "Name", null, true)]),
            JsonOptions);
        return (await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions))!;
    }

    private async Task<PrintJobResponse> SubmitAsync(int? printerId, params int[] quantities)
    {
        var template = await CreateTemplateAsync();
        var items = quantities
            .Select((quantity, index) => new PrintJobItemRequest(new() { ["name"] = $"Row {index + 1}" }, quantity))
            .ToList();

        var response = await _client.PostAsJsonAsync(
            "/api/print-jobs", new CreatePrintJobRequest(template.Id, printerId, null, items), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions))!;
    }

    private async Task<PrintJobResponse> WaitUntilFinishedAsync(int jobId)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            var job = await _client.GetFromJsonAsync<PrintJobResponse>($"/api/print-jobs/{jobId}", JsonOptions);

            if (job!.Status is "Completed" or "Failed" or "Cancelled")
            {
                return job;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Print job {jobId} did not finish.");
    }

    private static string[] Statuses(PrintJobResponse job) => job.Items.Select(item => item.Status).ToArray();

    private static int[] Rows(int count) => Enumerable.Repeat(1, count).ToArray();

    [Fact]
    public async Task ManyRows_GoToThePrinterInBatches_AndAllComeOut()
    {
        var printer = await CreateUsbPrinterAsync();
        var sentBefore = factory.UsbPort.Sent.Count;

        var job = await WaitUntilFinishedAsync((await SubmitAsync(printer.Id, Rows(60))).Id);

        Assert.Equal("Completed", job.Status);
        Assert.All(job.Items, item => Assert.Equal("Completed", item.Status));
        // 25 + 25 + 10 labels: three transmissions instead of sixty.
        Assert.Equal(3, factory.UsbPort.Sent.Count - sentBefore);
        Assert.All(job.Items, item => Assert.NotNull(item.PreviewUrl));
    }

    [Fact]
    public async Task ABatch_HoldsWholeRows_SoCopiesOfARowStayTogether()
    {
        var printer = await CreateUsbPrinterAsync();
        var sentBefore = factory.UsbPort.Sent.Count;

        // 20 + 10 would be 30: the second row starts a new batch. 40 copies are one batch of their own.
        var job = await WaitUntilFinishedAsync((await SubmitAsync(printer.Id, 20, 10, 40, 1)).Id);

        Assert.Equal("Completed", job.Status);
        Assert.Equal(4, factory.UsbPort.Sent.Count - sentBefore);
    }

    [Fact]
    public async Task AfterAFailedBatch_TheRestIsNotSent_AndCanBeReprinted()
    {
        var printer = await CreateUsbPrinterAsync();
        var sentBefore = factory.UsbPort.Sent.Count;
        // The first batch goes through, the second one fails (say, the tape ran out).
        factory.UsbPort.BeforeSend = sent => Task.FromResult(sent - sentBefore == 1 ? "The tape has run out." : null);

        try
        {
            var job = await WaitUntilFinishedAsync((await SubmitAsync(printer.Id, Rows(60))).Id);

            Assert.Equal("Failed", job.Status);
            Assert.Contains("rest of the job wasn't printed", job.ErrorMessage);
            Assert.Equal(
                [.. Enumerable.Repeat("Completed", 25), .. Enumerable.Repeat("Failed", 25), .. Enumerable.Repeat("Cancelled", 10)],
                Statuses(job));
            Assert.Contains("The tape has run out.", job.Items[25].ErrorMessage);
            Assert.Contains("earlier label failed", job.Items[59].ErrorMessage);
            Assert.Equal(1, factory.UsbPort.Sent.Count - sentBefore);

            factory.UsbPort.BeforeSend = null;
            var reprintResponse = await _client.PostAsync($"/api/print-jobs/{job.Id}/reprint-unprinted", null);
            Assert.Equal(HttpStatusCode.Created, reprintResponse.StatusCode);
            var reprint = await WaitUntilFinishedAsync((await reprintResponse.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions))!.Id);

            Assert.Equal("Completed", reprint.Status);
            Assert.Equal(35, reprint.Items.Count);
            Assert.Equal(("Row 26", "Row 60"), (reprint.Items[0].FieldValues["name"], reprint.Items[^1].FieldValues["name"]));
            Assert.Equal((job.PrinterId, job.Quality, job.CutMode), (reprint.PrinterId, reprint.Quality, reprint.CutMode));
        }
        finally
        {
            factory.UsbPort.BeforeSend = null;
        }
    }

    [Fact]
    public async Task StoppingAJobThatIsPrinting_LetsTheBatchAtThePrinterFinish_AndLeavesOutTheRest()
    {
        var printer = await CreateUsbPrinterAsync();
        var sentBefore = factory.UsbPort.Sent.Count;
        var atThePrinter = new TaskCompletionSource();
        var goOn = new TaskCompletionSource();
        factory.UsbPort.BeforeSend = async _ =>
        {
            atThePrinter.TrySetResult();
            await goOn.Task;
            return null;
        };

        try
        {
            var job = await SubmitAsync(printer.Id, Rows(60));
            await atThePrinter.Task.WaitAsync(TimeSpan.FromSeconds(30));

            var stop = await _client.PostAsync($"/api/print-jobs/{job.Id}/cancel", null);
            goOn.SetResult();
            var stopped = await WaitUntilFinishedAsync(job.Id);

            Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
            Assert.Equal("Cancelled", stopped.Status);
            Assert.Equal([.. Enumerable.Repeat("Completed", 25), .. Enumerable.Repeat("Cancelled", 35)], Statuses(stopped));
            Assert.Equal(1, factory.UsbPort.Sent.Count - sentBefore);
            Assert.Contains("stopped", stopped.ErrorMessage);
        }
        finally
        {
            goOn.TrySetResult();
            factory.UsbPort.BeforeSend = null;
        }
    }

    [Fact]
    public async Task StoppingAJobThatIsStillWaiting_CancelsItAtOnce()
    {
        var template = await CreateTemplateAsync();
        int jobId;

        // A job in the queue of another installation, so this one's processor leaves it waiting.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = new PrintJob
            {
                TemplateId = template.Id,
                TemplateVersionNumber = 1,
                InstanceId = Guid.NewGuid().ToString("N"),
                Items = [new PrintJobItem { FieldValuesJson = """{"name":"Hal"}""" }, new PrintJobItem { FieldValuesJson = """{"name":"Ida"}""" }]
            };
            db.PrintJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        var response = await _client.PostAsync($"/api/print-jobs/{jobId}/cancel", null);
        var cancelled = await response.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cancelled", cancelled!.Status);
        Assert.NotNull(cancelled.CompletedAt);
        Assert.Equal(["Cancelled", "Cancelled"], Statuses(cancelled));
    }

    [Fact]
    public async Task StoppingAndReprinting_LeaveAFinishedJobAlone()
    {
        var job = await WaitUntilFinishedAsync((await SubmitAsync(null, 1, 1)).Id);

        var stop = await _client.PostAsync($"/api/print-jobs/{job.Id}/cancel", null);
        var reprint = await _client.PostAsync($"/api/print-jobs/{job.Id}/reprint-unprinted", null);
        var unknownStop = await _client.PostAsync($"/api/print-jobs/{int.MaxValue}/cancel", null);
        var unknownReprint = await _client.PostAsync($"/api/print-jobs/{int.MaxValue}/reprint-unprinted", null);

        Assert.Equal("Completed", (await stop.Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions))!.Status);
        Assert.Equal(HttpStatusCode.BadRequest, reprint.StatusCode);
        Assert.Contains("Every row", await reprint.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknownStop.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownReprint.StatusCode);
    }

    [Fact]
    public async Task AJobWithoutAPrinter_RendersEveryRow()
    {
        var job = await WaitUntilFinishedAsync((await SubmitAsync(null, Rows(60))).Id);

        Assert.Equal("Completed", job.Status);
        Assert.All(job.Items, item => Assert.Equal("Completed", item.Status));
        Assert.All(job.Items, item => Assert.NotNull(item.PreviewUrl));
    }
}
