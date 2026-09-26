namespace Tapeory.Api.PrintJobs;

public sealed record CreatePrintJobResult(bool TemplateNotFound, IReadOnlyList<string> Errors, int? JobId)
{
    public static CreatePrintJobResult NotFound() => new(true, [], null);

    public static CreatePrintJobResult Invalid(IReadOnlyList<string> errors) => new(false, errors, null);

    public static CreatePrintJobResult Created(int jobId) => new(false, [], jobId);
}
