namespace Tapeory.Api.PrintData;

/// <param name="HeaderName">An optional request header, e.g. "Authorization" or "X-Api-Key".</param>
public sealed record FetchPrintDataRequest(
    int TemplateId,
    string? Url,
    string? HeaderName,
    string? HeaderValue,
    string? Separator,
    int? Sheet,
    string? Locale);

/// <summary>Gets the data for a bulk print from a web address (a REST endpoint answering JSON,
/// or a CSV, text or Excel file served over HTTP). The request is made by the server, so the
/// address must be reachable from where Tapeory runs.</summary>
public sealed class PrintDataFetcher(HttpClient http)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<(byte[]? Content, string? Error)> FetchAsync(string? url, string? headerName, string? headerValue, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
        {
            return (null, "Enter a web address that starts with http:// or https://.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/csv, text/plain, */*");

        if (!string.IsNullOrWhiteSpace(headerName) && !request.Headers.TryAddWithoutValidation(headerName.Trim(), headerValue ?? string.Empty))
        {
            return (null, $"\"{headerName}\" can't be sent as a request header.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                return (null, $"{address.Host} answered {(int)response.StatusCode} {response.ReasonPhrase}.".Replace("  ", " "));
            }

            if (response.Content.Headers.ContentLength > PrintDataReader.MaxSizeBytes)
            {
                return (null, TooLarge);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;

            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                buffer.Write(chunk, 0, read);

                if (buffer.Length > PrintDataReader.MaxSizeBytes)
                {
                    return (null, TooLarge);
                }
            }

            return buffer.Length == 0 ? (null, $"{address.Host} answered without any data.") : (buffer.ToArray(), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, $"{address.Host} didn't answer within {Timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return (null, $"{address.Host} couldn't be reached: {ex.Message}");
        }
    }

    private static string TooLarge => $"The answer is larger than {PrintDataReader.MaxSizeBytes / (1024 * 1024)} MB.";
}
