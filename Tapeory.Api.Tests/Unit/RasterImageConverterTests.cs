using SkiaSharp;
using Tapeory.Api.Uploads;

namespace Tapeory.Api.Tests.Unit;

public sealed class RasterImageConverterTests
{
    private static SKBitmap DecodePng(byte[] png)
    {
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        return SKBitmap.Decode(png);
    }

    [Fact]
    public void ToPng_ConvertsABilevelTiff_KeepingItsPixels()
    {
        using var bitmap = DecodePng(RasterImageConverter.ToPng(TestImages.BilevelTiff(16, 4))!);

        Assert.Equal(16, bitmap.Width);
        Assert.Equal(4, bitmap.Height);
        Assert.Equal(SKColors.Black, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.White, bitmap.GetPixel(15, 3));
    }

    [Fact]
    public void ToPng_ConvertsABmp_KeepingItsPixels()
    {
        using var bitmap = DecodePng(RasterImageConverter.ToPng(TestImages.Bmp(4, 2))!);

        Assert.Equal(4, bitmap.Width);
        Assert.Equal(2, bitmap.Height);
        Assert.Equal(SKColors.Black, bitmap.GetPixel(0, 1));
        Assert.Equal(SKColors.White, bitmap.GetPixel(3, 0));
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4, 5 })]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 42, 0, 9, 9, 9, 9 })] // TIFF header, then garbage
    public void ToPng_ReturnsNull_ForDataThatIsNotAnImage(byte[] data)
    {
        Assert.Null(RasterImageConverter.ToPng(data));
    }
}
