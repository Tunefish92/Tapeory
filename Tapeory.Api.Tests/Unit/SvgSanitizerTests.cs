using System.Text;
using Tapeory.Api.Uploads;

namespace Tapeory.Api.Tests.Unit;

public sealed class SvgSanitizerTests
{
    private static byte[] Bytes(string svg) => Encoding.UTF8.GetBytes(svg);

    [Fact]
    public void Sanitize_RemovesScriptElements()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg"><script>alert('xss')</script><rect width="10" height="10"/></svg>
            """;

        var result = SvgSanitizer.Sanitize(Bytes(svg));

        Assert.True(result.IsValid);
        var output = Encoding.UTF8.GetString(result.SanitizedContent!);
        Assert.DoesNotContain("script", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rect", output);
    }

    [Fact]
    public void Sanitize_RemovesEventHandlerAttributes()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg"><rect width="10" height="10" onload="alert('xss')"/></svg>
            """;

        var result = SvgSanitizer.Sanitize(Bytes(svg));

        Assert.True(result.IsValid);
        var output = Encoding.UTF8.GetString(result.SanitizedContent!);
        Assert.DoesNotContain("onload", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_RemovesJavascriptUriAttributes()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg"><a href="javascript:alert('xss')"><rect width="10" height="10"/></a></svg>
            """;

        var result = SvgSanitizer.Sanitize(Bytes(svg));

        Assert.True(result.IsValid);
        var output = Encoding.UTF8.GetString(result.SanitizedContent!);
        Assert.DoesNotContain("javascript:", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_RemovesForeignObjectElements()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg"><foreignObject><body xmlns="http://www.w3.org/1999/xhtml"><script>alert(1)</script></body></foreignObject></svg>
            """;

        var result = SvgSanitizer.Sanitize(Bytes(svg));

        Assert.True(result.IsValid);
        var output = Encoding.UTF8.GetString(result.SanitizedContent!);
        Assert.DoesNotContain("foreignObject", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("script", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_PreservesBenignContent()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><circle cx="5" cy="5" r="4" fill="#ff0000"/></svg>
            """;

        var result = SvgSanitizer.Sanitize(Bytes(svg));

        Assert.True(result.IsValid);
        var output = Encoding.UTF8.GetString(result.SanitizedContent!);
        Assert.Contains("circle", output);
        Assert.Contains("#ff0000", output);
    }

    [Fact]
    public void Sanitize_RejectsInvalidXml()
    {
        var result = SvgSanitizer.Sanitize(Bytes("this is not xml at all"));

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.Null(result.SanitizedContent);
    }

    [Fact]
    public void Sanitize_RejectsNonSvgBinaryContent()
    {
        var result = SvgSanitizer.Sanitize([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Assert.False(result.IsValid);
    }
}
