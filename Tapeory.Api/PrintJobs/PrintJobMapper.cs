using System.Text.Json;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.PrintJobs;

public static class PrintJobMapper
{
    public static PrintJobItemResponse ToItemResponse(PrintJobItem item) => new(
        item.Id,
        DeserializeFieldValues(item.FieldValuesJson),
        item.Quantity,
        item.Status.ToString(),
        item.ErrorMessage,
        item.RenderedImageFileId is null ? null : $"/api/print-jobs/items/{item.Id}/preview");

    public static PrintJobResponse ToResponse(PrintJob job)
    {
        var templateName = job.Template?.Name
            ?? throw new InvalidOperationException($"PrintJob {job.Id} has no Template loaded.");

        return new PrintJobResponse(
            job.Id,
            job.TemplateId,
            templateName,
            job.Template.DeletedAt is not null,
            job.TemplateVersionNumber,
            job.PrinterId,
            job.PrinterName,
            job.Quality.ToString(),
            job.CutMode.ToString(),
            job.Status.ToString(),
            job.ErrorMessage,
            job.CreatedAt,
            job.CompletedAt,
            [.. job.Items.Select(ToItemResponse)]);
    }

    private static Dictionary<string, string> DeserializeFieldValues(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
