using Tapeory.Api.Storage;

namespace Tapeory.Api.Tests.Unit;

public sealed class StoragePathGuardTests
{
    [Fact]
    public void ResolveWithinRoot_ReturnsFullPath_ForOrdinaryRelativePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "tapeory-guard-root");

        var result = StoragePathGuard.ResolveWithinRoot(root, "images/file.png");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "images", "file.png")),
            result);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("../../outside.png")]
    [InlineData("images/../../outside.png")]
    public void ResolveWithinRoot_Throws_WhenRelativePathEscapesRoot(string maliciousPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "tapeory-guard-root");

        Assert.Throws<UnauthorizedAccessException>(
            () => StoragePathGuard.ResolveWithinRoot(root, maliciousPath));
    }

    [Fact]
    public void ResolveWithinRoot_Throws_WhenGivenAnAbsolutePathOutsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tapeory-guard-root");
        var absoluteElsewhere = Path.Combine(Path.GetTempPath(), "somewhere-else", "file.png");

        Assert.Throws<UnauthorizedAccessException>(
            () => StoragePathGuard.ResolveWithinRoot(root, absoluteElsewhere));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveWithinRoot_Throws_WhenRelativePathIsBlank(string blankPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "tapeory-guard-root");

        Assert.Throws<UnauthorizedAccessException>(
            () => StoragePathGuard.ResolveWithinRoot(root, blankPath));
    }
}
