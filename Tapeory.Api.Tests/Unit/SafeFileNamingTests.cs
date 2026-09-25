using Tapeory.Api.Storage;

namespace Tapeory.Api.Tests.Unit;

public sealed class SafeFileNamingTests
{
    [Theory]
    [InlineData("photo.png", ".png")]
    [InlineData("PHOTO.PNG", ".png")]
    [InlineData("archive.tar.gz", ".gz")]
    [InlineData("no-extension", "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void GetSafeExtension_ReturnsLowercaseExtension(string input, string expected)
    {
        Assert.Equal(expected, SafeFileNaming.GetSafeExtension(input));
    }

    [Theory]
    [InlineData("../../etc/passwd.png", ".png")]
    [InlineData("..\\..\\windows\\win.ini", ".ini")]
    public void GetSafeExtension_IgnoresDirectoryTraversalPrefixes(string input, string expected)
    {
        Assert.Equal(expected, SafeFileNaming.GetSafeExtension(input));
    }

    [Fact]
    public void GenerateStoredFileName_PreservesExtensionButNotOriginalName()
    {
        var result = SafeFileNaming.GenerateStoredFileName("my secret document.png");

        Assert.EndsWith(".png", result);
        Assert.DoesNotContain("secret", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(' ', result);
    }

    [Fact]
    public void GenerateStoredFileName_IsUniqueAcrossCalls()
    {
        var first = SafeFileNaming.GenerateStoredFileName("a.png");
        var second = SafeFileNaming.GenerateStoredFileName("a.png");

        Assert.NotEqual(first, second);
    }
}
