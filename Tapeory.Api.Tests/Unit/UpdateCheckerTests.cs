using System.Net;
using System.Text;
using Tapeory.Api.Updates;

namespace Tapeory.Api.Tests.Unit;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("0.3.0", "0.2.1", true)]
    [InlineData("v0.2.10", "0.2.9", true)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.2.1", "0.2.1", false)]
    [InlineData("0.2.0", "0.2.1", false)]
    [InlineData("0.3.0-beta", "0.2.1", true)]
    [InlineData("0.6.0", "0.6.0-beta.1", true)]
    [InlineData("0.6.0-beta.2", "0.6.0-beta.1", false)]
    [InlineData("0.5.0", "0.6.0-beta.1", false)]
    [InlineData("0.6.0", "0.6.0", false)]
    [InlineData(null, "0.2.1", false)]
    [InlineData("nonsense", "0.2.1", false)]
    public void IsNewer_ComparesReleaseVersions(string? latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(latest, current));
    }

    [Fact]
    public async Task Check_ReadsTheLatestReleaseAndCachesIt()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"tag_name":"v99.0.0","html_url":"https://github.com/Tunefish92/Tapeory/releases/tag/v99.0.0"}""");
        var time = new ManualTime(DateTimeOffset.Parse("2026-09-27T10:00:00Z"));
        var checker = new UpdateChecker(new HttpClient(handler), time);

        var result = await checker.CheckAsync(refresh: false, CancellationToken.None);

        Assert.Equal("99.0.0", result.LatestVersion);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("https://github.com/Tunefish92/Tapeory/releases/tag/v99.0.0", result.ReleaseUrl);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(UpdateChecker.LatestReleaseUrl, handler.LastRequest!.RequestUri!.ToString());
        Assert.NotEmpty(handler.LastRequest.Headers.UserAgent);

        await checker.CheckAsync(refresh: false, CancellationToken.None);
        Assert.Equal(1, handler.Calls);

        await checker.CheckAsync(refresh: true, CancellationToken.None);
        Assert.Equal(2, handler.Calls);

        time.Advance(TimeSpan.FromHours(7));
        await checker.CheckAsync(refresh: false, CancellationToken.None);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Check_ListsTheReleaseFilesWithTheirDigests()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"tag_name":"v99.0.0","html_url":"https://github.com/Tunefish92/Tapeory/releases/tag/v99.0.0","assets":[
              {"name":"Tapeory-99.0.0-windows-x64-setup.exe","size":1234,
               "browser_download_url":"https://github.com/Tunefish92/Tapeory/releases/download/v99.0.0/Tapeory-99.0.0-windows-x64-setup.exe",
               "digest":"sha256:ab12"},
              {"name":"Tapeory-99.0.0-linux-x86_64.AppImage","size":5678,
               "browser_download_url":"https://github.com/Tunefish92/Tapeory/releases/download/v99.0.0/Tapeory-99.0.0-linux-x86_64.AppImage"}
            ]}
            """);
        var checker = new UpdateChecker(new HttpClient(handler), TimeProvider.System);

        var result = await checker.CheckAsync(refresh: false, CancellationToken.None);

        Assert.NotNull(result.Assets);
        Assert.Equal(2, result.Assets!.Count);
        Assert.Equal(new ReleaseAsset("Tapeory-99.0.0-windows-x64-setup.exe",
            "https://github.com/Tunefish92/Tapeory/releases/download/v99.0.0/Tapeory-99.0.0-windows-x64-setup.exe", 1234, "ab12"),
            result.Assets[0]);
        Assert.Null(result.Assets[1].Sha256);
    }

    [Fact]
    public async Task Check_ReportsAFailureWithoutCachingIt()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden, "{}");
        var checker = new UpdateChecker(new HttpClient(handler), TimeProvider.System);

        var result = await checker.CheckAsync(refresh: false, CancellationToken.None);

        Assert.Null(result.LatestVersion);
        Assert.False(result.UpdateAvailable);
        Assert.NotNull(result.ErrorMessage);

        await checker.CheckAsync(refresh: false, CancellationToken.None);
        Assert.Equal(2, handler.Calls);
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
