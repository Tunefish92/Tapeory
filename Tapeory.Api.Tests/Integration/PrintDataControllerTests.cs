using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Tapeory.Api.PrintData;
using Tapeory.Api.Templates;
using Tapeory.Api.Tests.Unit;

namespace Tapeory.Api.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class PrintDataControllerTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>A 40 × 12 mm label with a name (text that shrinks to fit a 20 × 4 mm box), an
    /// EAN-13 barcode and an optional note.</summary>
    private async Task<TemplateDetailResponse> CreateTemplateAsync()
    {
        var editorJson = """
            {"formatVersion":1,"widthMm":40,"heightMm":12,"objects":[
                {"type":"dynamicField","id":"f1","x":1,"y":1,"rotation":0,"fieldName":"name",
                 "width":20,"height":4,"fontSize":10,"fontFamily":"Inter","fontWeight":"normal",
                 "align":"left","fill":"#000000","fit":"shrink"},
                {"type":"barcode","id":"b1","x":22,"y":1,"width":17,"height":10,"rotation":0,
                 "symbology":"ean13","data":"400638133393","fieldName":"gtin","showText":false,"fill":"#000000"}
            ]}
            """;

        var response = await _client.PostAsJsonAsync(
            "/api/templates",
            new CreateTemplateRequest(
                $"Bulk-{Guid.NewGuid():N}", null, null, null, 40m, 12m, editorJson,
                [new TemplateFieldDto("name", "Name", null, true), new TemplateFieldDto("gtin", "GTIN", null, false)]),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions))!;
    }

    private async Task<HttpResponseMessage> ParseAsync(
        int templateId, byte[] file, string fileName, string? separator = null, int? sheet = null, string? locale = null)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", fileName);
        form.Add(new StringContent(templateId.ToString()), "templateId");
        if (separator is not null) form.Add(new StringContent(separator), "separator");
        if (sheet is not null) form.Add(new StringContent(sheet.Value.ToString()), "sheet");
        if (locale is not null) form.Add(new StringContent(locale), "locale");
        return await _client.PostAsync("/api/print-data/parse", form);
    }

    private static async Task<PrintDataResponse> DataAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrintDataResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task Parse_ReadsACsvFile_AndMatchesItsHeaderToTheFields()
    {
        var template = await CreateTemplateAsync();

        var data = await DataAsync(await ParseAsync(
            template.Id, Encoding.UTF8.GetBytes("GTIN;Name;Copies\n4006381333931;Box;2\n;Bag;1\n"), "stock.csv"));

        Assert.Equal((";", true, true), (data.Separator, data.SeparatorDetected, data.HasHeader));
        Assert.Equal(new Dictionary<string, int> { ["name"] = 1, ["gtin"] = 0 }, data.Fields);
        Assert.Equal(2, data.QuantityColumn);
        Assert.Equal(["", "Bag", "1"], data.Rows[2]);
    }

    [Fact]
    public async Task Parse_SaysWhenItCouldNotTellTheSeparator_AndTakesTheUsersChoice()
    {
        var template = await CreateTemplateAsync();
        var file = Encoding.UTF8.GetBytes("Smith, Anna;4006381333931\nJones, Ben;4006381333931\n");

        var unsure = await DataAsync(await ParseAsync(template.Id, file, "names.txt"));
        var chosen = await DataAsync(await ParseAsync(template.Id, file, "names.txt", separator: ";"));

        Assert.False(unsure.SeparatorDetected);
        Assert.Equal((";", false), (chosen.Separator, chosen.HasHeader));
        Assert.Empty(chosen.Fields);
        Assert.Equal(["Smith, Anna", "4006381333931"], chosen.Rows[0]);
    }

    [Fact]
    public async Task Parse_ReadsAnExcelFile_InTheReadersRegion()
    {
        var template = await CreateTemplateAsync();
        var workbook = TestWorkbooks.Create(
            ("Notes", [["nothing here that names a field"]]),
            ("Stock", [["Name", "GTIN"], [(46294d, TestWorkbooks.ShortDate), 4006381333931d]]));

        var first = await DataAsync(await ParseAsync(template.Id, workbook, "stock.xlsx", locale: "de-DE"));
        var second = await DataAsync(await ParseAsync(template.Id, workbook, "stock.xlsx", sheet: 1, locale: "de-DE"));

        Assert.Equal(("spreadsheet", 0, false), (first.Kind, first.Sheet, first.HasHeader));
        Assert.Equal(["Notes", "Stock"], first.Sheets);
        Assert.Equal((1, true), (second.Sheet, second.HasHeader));
        Assert.Equal(["29.09.2026", "4006381333931"], second.Rows[1]);
    }

    [Fact]
    public async Task Parse_RefusesWhatItCannotRead_AndSaysWhy()
    {
        var template = await CreateTemplateAsync();

        var empty = await ParseAsync(template.Id, [], "empty.csv");
        var binary = await ParseAsync(template.Id, [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 1], "logo.png");
        var unknownTemplate = await ParseAsync(int.MaxValue, "a;b"u8.ToArray(), "data.csv");

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, binary.StatusCode);
        Assert.Contains("isn't a text file", await binary.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknownTemplate.StatusCode);
    }

    [Fact]
    public async Task Parse_RefusesAFileOverTheSizeLimit_AndSaysSo()
    {
        var template = await CreateTemplateAsync();
        var tooLarge = new byte[PrintDataReader.MaxSizeBytes + 1];
        Array.Fill(tooLarge, (byte)'a');

        var response = await ParseAsync(template.Id, tooLarge, "huge.txt");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("larger than 100 MB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CheckRows_ReportsEachRowsProblems()
    {
        var template = await CreateTemplateAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/templates/{template.Id}/check-rows",
            new CheckRowsRequest([
                new() { ["name"] = "Box", ["gtin"] = "4006381333931" },
                new() { ["name"] = "", ["gtin"] = "4006381333931" },
                new() { ["name"] = "Bag", ["gtin"] = "12AB" },
                new() { ["name"] = "A very long name that can never fit into a box of twenty millimetres width, however small" },
                null,
            ]),
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = (await response.Content.ReadFromJsonAsync<CheckRowsResponse>(JsonOptions))!.Rows;
        Assert.Equal(5, rows.Count);
        Assert.Equal((0, 0), (rows[0].Errors.Count, rows[0].Warnings.Count));
        Assert.Contains("name", Assert.Single(rows[1].Errors));
        Assert.Contains("EAN-13", Assert.Single(rows[2].Errors));
        Assert.Empty(rows[3].Errors);
        Assert.Contains("hard to read", Assert.Single(rows[3].Warnings));
        Assert.Contains("name", Assert.Single(rows[4].Errors));
    }

    [Fact]
    public async Task CheckRows_TakesALimitedNumberOfRowsAtOnce_AndKnowsOnlyExistingTemplates()
    {
        var template = await CreateTemplateAsync();
        var tooMany = Enumerable.Repeat<Dictionary<string, string>?>(new() { ["name"] = "x" }, RowChecker.MaxRowsPerRequest + 1).ToList();

        var refused = await _client.PostAsJsonAsync($"/api/templates/{template.Id}/check-rows", new CheckRowsRequest(tooMany), JsonOptions);
        var unknown = await _client.PostAsJsonAsync($"/api/templates/{int.MaxValue}/check-rows", new CheckRowsRequest([]), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    /// <summary>A small web server for one test: answers every request with what
    /// <paramref name="respond"/> writes, and remembers the last request's headers.</summary>
    private sealed class TestEndpoint : IDisposable
    {
        private readonly System.Net.HttpListener _listener = new();

        public string Url { get; }

        public System.Collections.Specialized.NameValueCollection? LastHeaders { get; private set; }

        public TestEndpoint(Action<System.Net.HttpListenerResponse> respond)
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            Url = $"http://127.0.0.1:{port}/api/items";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();
                        LastHeaders = context.Request.Headers;
                        respond(context.Response);
                        context.Response.Close();
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
            });
        }

        public static void Write(System.Net.HttpListenerResponse response, string body, string contentType = "application/json")
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            response.ContentType = contentType;
            response.OutputStream.Write(bytes);
        }

        public void Dispose() => _listener.Close();
    }

    private Task<HttpResponseMessage> FetchAsync(int templateId, string? url, string? headerName = null, string? headerValue = null) =>
        _client.PostAsJsonAsync("/api/print-data/fetch", new FetchPrintDataRequest(templateId, url, headerName, headerValue, null, null, null), JsonOptions);

    [Fact]
    public async Task Parse_ReadsAJsonFile()
    {
        var template = await CreateTemplateAsync();

        var data = await DataAsync(await ParseAsync(
            template.Id, """[{"gtin":"4006381333931","name":"Box"},{"gtin":"4006381333931","name":"Bag"}]"""u8.ToArray(), "stock.json"));

        Assert.Equal(("json", true), (data.Kind, data.HasHeader));
        Assert.Equal(new Dictionary<string, int> { ["name"] = 1, ["gtin"] = 0 }, data.Fields);
        Assert.Equal(["4006381333931", "Bag"], data.Rows[2]);
    }

    [Fact]
    public async Task Fetch_GetsRecordsFromARestEndpoint_AndSendsTheGivenHeader()
    {
        var template = await CreateTemplateAsync();
        using var endpoint = new TestEndpoint(response =>
            TestEndpoint.Write(response, """{"items":[{"name":"Box","gtin":"4006381333931"},{"name":"Bag","gtin":"4006381333931"}]}"""));

        var data = await DataAsync(await FetchAsync(template.Id, endpoint.Url, "X-Api-Key", "secret"));

        Assert.Equal("json", data.Kind);
        Assert.Equal(new Dictionary<string, int> { ["name"] = 0, ["gtin"] = 1 }, data.Fields);
        Assert.Equal(3, data.Rows.Count);
        Assert.Equal("secret", endpoint.LastHeaders!["X-Api-Key"]);
    }

    [Fact]
    public async Task Fetch_AlsoReadsCsvServedOverHttp()
    {
        var template = await CreateTemplateAsync();
        using var endpoint = new TestEndpoint(response => TestEndpoint.Write(response, "Name;GTIN\nBox;4006381333931\n", "text/csv"));

        var data = await DataAsync(await FetchAsync(template.Id, endpoint.Url));

        Assert.Equal(("text", ";", true), (data.Kind, data.Separator, data.HasHeader));
    }

    [Fact]
    public async Task Fetch_SaysWhatWentWrong()
    {
        var template = await CreateTemplateAsync();
        using var failing = new TestEndpoint(response => response.StatusCode = 401);
        using var empty = new TestEndpoint(_ => { });
        string closedUrl;
        using (var closed = new TestEndpoint(_ => { }))
        {
            closedUrl = closed.Url;
        }

        var unauthorized = await FetchAsync(template.Id, failing.Url);
        var noData = await FetchAsync(template.Id, empty.Url);
        var unreachable = await FetchAsync(template.Id, closedUrl);
        var notAnAddress = await FetchAsync(template.Id, "file:///etc/passwd");
        var noAddress = await FetchAsync(template.Id, " ");
        var unknownTemplate = await FetchAsync(int.MaxValue, failing.Url);

        Assert.Equal(HttpStatusCode.BadRequest, unauthorized.StatusCode);
        Assert.Contains("answered 401", await unauthorized.Content.ReadAsStringAsync());
        Assert.Contains("without any data", await noData.Content.ReadAsStringAsync());
        Assert.Contains("couldn't be reached", await unreachable.Content.ReadAsStringAsync());
        Assert.Contains("http://", await notAnAddress.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, noAddress.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownTemplate.StatusCode);
    }
}
