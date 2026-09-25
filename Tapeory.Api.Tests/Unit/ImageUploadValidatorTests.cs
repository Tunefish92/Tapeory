using Tapeory.Api.Uploads;

namespace Tapeory.Api.Tests.Unit;

public sealed class ImageUploadValidatorTests
{
    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    [InlineData("image/svg+xml")]
    public void Validate_AcceptsAllowedContentTypesWithinSizeLimit(string contentType)
    {
        var result = ImageUploadValidator.Validate(contentType, sizeBytes: 1024);

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/html")]
    [InlineData("image/gif")]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_RejectsDisallowedContentTypes(string? contentType)
    {
        var result = ImageUploadValidator.Validate(contentType, sizeBytes: 1024);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Validate_RejectsFilesOverTheSizeLimit()
    {
        var result = ImageUploadValidator.Validate("image/png", ImageUploadValidator.MaxSizeBytes + 1);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_AcceptsFileExactlyAtTheSizeLimit()
    {
        var result = ImageUploadValidator.Validate("image/png", ImageUploadValidator.MaxSizeBytes);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsEmptyFiles()
    {
        var result = ImageUploadValidator.Validate("image/png", sizeBytes: 0);

        Assert.False(result.IsValid);
    }
}
