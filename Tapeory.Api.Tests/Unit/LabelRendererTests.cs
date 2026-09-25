using Tapeory.Api.Rendering;
using SkiaSharp;

namespace Tapeory.Api.Tests.Unit;

public sealed class LabelRendererTests
{
    private const float PxPerMmAt300Dpi = 300f / 25.4f;

    private static readonly Dictionary<string, string> NoFieldValues = [];
    private static readonly LabelRenderer Renderer = new();

    private static byte[]? NoImages(int _) => null;

    private static SKBitmap DecodePng(byte[] pngBytes)
    {
        var bitmap = SKBitmap.Decode(pngBytes);
        Assert.NotNull(bitmap);
        return bitmap;
    }

    private static SKColor PixelAtMm(SKBitmap bitmap, decimal xMm, decimal yMm)
    {
        var px = (int)((float)xMm * PxPerMmAt300Dpi);
        var py = (int)((float)yMm * PxPerMmAt300Dpi);
        return bitmap.GetPixel(Math.Clamp(px, 0, bitmap.Width - 1), Math.Clamp(py, 0, bitmap.Height - 1));
    }

    private static bool IsWhite(SKColor color) => color.Red > 250 && color.Green > 250 && color.Blue > 250;

    private static byte[] SolidColorPng(SKColor color, int size = 10)
    {
        using var bitmap = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static RenderableDocument DocumentWith(decimal widthMm, decimal heightMm, params RenderableObject[] objects) =>
        new(widthMm, heightMm, [.. objects]);

    [Fact]
    public void RenderPng_ProducesAnImageOfTheExpectedPixelDimensions()
    {
        var document = DocumentWith(50m, 25m);

        var bytes = Renderer.RenderPng(document, NoFieldValues, NoImages);
        using var bitmap = DecodePng(bytes);

        Assert.Equal((int)MathF.Ceiling(50f * PxPerMmAt300Dpi), bitmap.Width);
        Assert.Equal((int)MathF.Ceiling(25f * PxPerMmAt300Dpi), bitmap.Height);
    }

    [Fact]
    public void RenderPng_BackgroundIsWhite_WhenThereAreNoObjects()
    {
        var document = DocumentWith(50m, 25m);

        var bytes = Renderer.RenderPng(document, NoFieldValues, NoImages);
        using var bitmap = DecodePng(bytes);

        Assert.True(IsWhite(PixelAtMm(bitmap, 25m, 12m)));
    }

    [Fact]
    public void RenderPng_FillsARectWithTheGivenColor()
    {
        var rect = new RenderableRect(0, 0, 0, false, false, 50, 25, "#ff0000", "transparent", 0, 0);
        var document = DocumentWith(50m, 25m, rect);

        var bytes = Renderer.RenderPng(document, NoFieldValues, NoImages);
        using var bitmap = DecodePng(bytes);

        var pixel = PixelAtMm(bitmap, 25m, 12m);
        Assert.True(pixel.Red > 200 && pixel.Green < 50 && pixel.Blue < 50);
    }

    [Fact]
    public void RenderPng_SkipsHiddenObjects()
    {
        var rect = new RenderableRect(0, 0, 0, false, true, 50, 25, "#ff0000", "transparent", 0, 0);
        var document = DocumentWith(50m, 25m, rect);

        var bytes = Renderer.RenderPng(document, NoFieldValues, NoImages);
        using var bitmap = DecodePng(bytes);

        Assert.True(IsWhite(PixelAtMm(bitmap, 25m, 12m)));
    }

    [Fact]
    public void RenderPng_StillDrawsLockedObjects()
    {
        var rect = new RenderableRect(0, 0, 0, true, false, 50, 25, "#ff0000", "transparent", 0, 0);
        var document = DocumentWith(50m, 25m, rect);

        var bytes = Renderer.RenderPng(document, NoFieldValues, NoImages);
        using var bitmap = DecodePng(bytes);

        var pixel = PixelAtMm(bitmap, 25m, 12m);
        Assert.True(pixel.Red > 200 && pixel.Green < 50);
    }

    [Fact]
    public void RenderPng_DrawsNothingForADynamicField_WhenNoValueIsProvided()
    {
        var field = new RenderableDynamicField(2, 2, 0, false, false, "name", 46, 20, 20, "Arial", "bold", "left", "#000000");
        var document = DocumentWith(50m, 25m, field);

        var bytes = Renderer.RenderPng(document, NoFieldValues, NoImages);
        using var bitmap = DecodePng(bytes);

        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                Assert.True(IsWhite(bitmap.GetPixel(x, y)));
            }
        }
    }

    [Fact]
    public void RenderPng_DrawsTheSubstitutedValue_ForADynamicField()
    {
        var field = new RenderableDynamicField(2, 2, 0, false, false, "name", 46, 20, 20, "Arial", "bold", "left", "#000000");
        var document = DocumentWith(50m, 25m, field);

        var bytes = Renderer.RenderPng(document, new Dictionary<string, string> { ["name"] = "X" }, NoImages);
        using var bitmap = DecodePng(bytes);

        var anyNonWhitePixel = false;

        for (var x = 0; x < bitmap.Width && !anyNonWhitePixel; x++)
        {
            for (var y = 0; y < bitmap.Height && !anyNonWhitePixel; y++)
            {
                if (!IsWhite(bitmap.GetPixel(x, y)))
                {
                    anyNonWhitePixel = true;
                }
            }
        }

        Assert.True(anyNonWhitePixel, "Expected at least one non-white pixel where the substituted text was drawn.");
    }

    [Fact]
    public void RenderPng_DoesNotThrow_ForARotatedObject()
    {
        var rect = new RenderableRect(10, 5, 45, false, false, 10, 10, "#0000ff", "transparent", 0, 0);
        var document = DocumentWith(50m, 25m, rect);

        var bytes = Renderer.RenderPng(document, NoFieldValues, NoImages);

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void RenderPng_DrawsARasterImage_AtItsPosition()
    {
        var image = new RenderableImage(5, 5, 0, false, false, 15, 15, 1, "/api/uploads/images/1");
        var document = DocumentWith(50m, 25m, image);

        var bytes = Renderer.RenderPng(
            document, NoFieldValues, id => id == 1 ? SolidColorPng(SKColors.Blue) : null);
        using var bitmap = DecodePng(bytes);

        var pixel = PixelAtMm(bitmap, 12.5m, 12.5m); // center of the 5..20mm image box
        Assert.True(pixel.Blue > 200 && pixel.Red < 50);
    }

    [Fact]
    public void RenderPng_SkipsAnImage_WhenTheResolverReturnsNull()
    {
        var image = new RenderableImage(5, 5, 0, false, false, 15, 15, 999, "/api/uploads/images/999");
        var document = DocumentWith(50m, 25m, image);

        var bytes = Renderer.RenderPng(document, NoFieldValues, NoImages);

        Assert.NotEmpty(bytes); // must not throw
    }

    [Fact]
    public void RenderPng_RendersAnSvgImage()
    {
        const string redSquareSvg =
            """<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><rect width="100" height="100" fill="#ff0000"/></svg>""";

        var image = new RenderableImage(5, 5, 0, false, false, 15, 15, 1, "/api/uploads/images/1");
        var document = DocumentWith(50m, 25m, image);

        var bytes = Renderer.RenderPng(
            document, NoFieldValues, id => id == 1 ? System.Text.Encoding.UTF8.GetBytes(redSquareSvg) : null);
        using var bitmap = DecodePng(bytes);

        var pixel = PixelAtMm(bitmap, 12.5m, 12.5m);
        Assert.True(pixel.Red > 200 && pixel.Green < 50 && pixel.Blue < 50);
    }

    [Fact]
    public void RenderPdf_ProducesBytesStartingWithThePdfMagicHeader()
    {
        var document = DocumentWith(50m, 25m, new RenderableRect(0, 0, 0, false, false, 50, 25, "#ff0000", "transparent", 0, 0));

        var bytes = Renderer.RenderPdf(document, NoFieldValues, NoImages);

        Assert.True(bytes.Length > 4);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Theory]
    [InlineData(TextFitMode.Shrink)]
    [InlineData(TextFitMode.Wrap)]
    public void RenderPng_KeepsFixedSizeTextInsideItsBox_HoweverLongTheValue(TextFitMode fit)
    {
        // Box: x 5..25 mm, y 5..10 mm. The value would be ~200 mm wide at 40pt.
        var field = new RenderableDynamicField(5, 5, 0, false, false, "name", 20, 5, 40, "Arial", "bold", "left", "#000000", fit);
        var document = DocumentWith(50m, 25m, field);
        var value = "Strawberry rhubarb jam with vanilla, made on the 12th of June";

        var bytes = Renderer.RenderPng(document, new Dictionary<string, string> { ["name"] = value }, NoImages);
        using var bitmap = DecodePng(bytes);

        // Allow one pixel of anti-aliasing slack around the box edges.
        var left = (int)(5f * PxPerMmAt300Dpi) - 1;
        var right = (int)(25f * PxPerMmAt300Dpi) + 1;
        var top = (int)(5f * PxPerMmAt300Dpi) - 1;
        var bottom = (int)(10f * PxPerMmAt300Dpi) + 1;

        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (x < left || x > right || y < top || y > bottom)
                {
                    Assert.True(IsWhite(bitmap.GetPixel(x, y)), $"ink outside the box at {x},{y}");
                }
            }
        }
    }
}
