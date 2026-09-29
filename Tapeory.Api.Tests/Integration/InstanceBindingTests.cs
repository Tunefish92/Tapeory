using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Instances;
using Tapeory.Api.PrintJobs;
using Tapeory.Api.Printers;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Tests.Integration;

/// <summary>
/// Several Tapeory installations sharing one database (the desktop app and a Docker server): each
/// prints only its own jobs, and a USB printer only from the computer it's connected to.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class InstanceBindingTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<TemplateDetailResponse> CreateTemplateAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/templates",
            new CreateTemplateRequest(
                $"Instance-{Guid.NewGuid():N}", null, null, null, 40m, 20m,
                """{"formatVersion":1,"widthMm":40,"heightMm":20,"objects":[]}""", null),
            JsonOptions);
        return (await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task NewJobs_BelongToThisInstallation()
    {
        var template = await CreateTemplateAsync();

        var job = await (await _client.PostAsJsonAsync(
                "/api/print-jobs",
                new CreatePrintJobRequest(template.Id, null, null, [new PrintJobItemRequest([], 1)]),
                JsonOptions))
            .Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions);

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PrintJobs.SingleAsync(j => j.Id == job!.Id);
        Assert.Equal(factory.Services.GetRequiredService<TapeoryInstance>().Id, stored.InstanceId);
    }

    [Fact]
    public async Task AnotherInstallationsJob_IsNeverPrintedHere()
    {
        var template = await CreateTemplateAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new PrintJob
        {
            TemplateId = template.Id,
            TemplateVersionNumber = 1,
            InstanceId = "another-installation",
            Items = [new PrintJobItem { FieldValuesJson = "{}", Quantity = 1 }],
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();

        try
        {
            // The queue polls every 2 seconds; give it a few rounds.
            await Task.Delay(TimeSpan.FromSeconds(5));
            await db.Entry(job).ReloadAsync();
            Assert.Equal(PrintJobStatus.Queued, job.Status);
        }
        finally
        {
            db.PrintJobs.Remove(job);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task UsbPrinters_BelongToTheirComputer()
    {
        var created = await (await _client.PostAsJsonAsync(
                "/api/printers",
                new CreatePrinterRequest("USB P750W", "PT-P750W", "Usb", null, null, null, "usb-1", null, null, true),
                JsonOptions))
            .Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions);
        Assert.True(created!.OnThisComputer);
        Assert.Equal(Environment.MachineName, created.ComputerName);

        // As if another computer had added it.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Printers.Where(p => p.Id == created.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.InstanceId, "another-installation")
                .SetProperty(p => p.ComputerName, "OFFICE-PC"));
        }

        try
        {
            var seen = await _client.GetFromJsonAsync<PrinterResponse>($"/api/printers/{created.Id}", JsonOptions);
            Assert.False(seen!.OnThisComputer);
            Assert.Equal("OFFICE-PC", seen.ComputerName);

            var testPrint = await _client.PostAsync($"/api/printers/{created.Id}/test-print", null);
            Assert.Equal(HttpStatusCode.Conflict, testPrint.StatusCode);
            Assert.Contains("OFFICE-PC", await testPrint.Content.ReadAsStringAsync());

            var template = await CreateTemplateAsync();
            var job = await _client.PostAsJsonAsync(
                "/api/print-jobs",
                new CreatePrintJobRequest(template.Id, created.Id, null, [new PrintJobItemRequest([], 1)]),
                JsonOptions);
            Assert.Equal(HttpStatusCode.BadRequest, job.StatusCode);
            Assert.Contains("OFFICE-PC", await job.Content.ReadAsStringAsync());
        }
        finally
        {
            await _client.DeleteAsync($"/api/printers/{created.Id}");
        }
    }

    [Fact]
    public async Task NetworkPrinters_AreEveryInstallations()
    {
        var created = await (await _client.PostAsJsonAsync(
                "/api/printers",
                new CreatePrinterRequest("Network P750W", "PT-P750W", "IpAddress", "192.0.2.10", 9100, null, null, null, null, true),
                JsonOptions))
            .Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions);

        try
        {
            Assert.True(created!.OnThisComputer);
            Assert.Null(created.ComputerName);
        }
        finally
        {
            await _client.DeleteAsync($"/api/printers/{created!.Id}");
        }
    }
}
