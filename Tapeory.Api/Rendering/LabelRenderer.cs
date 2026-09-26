using SkiaSharp;
using Svg.Skia;

namespace Tapeory.Api.Rendering;

/// <summary>Looks up the raw bytes of an uploaded image by id, or null if unavailable. Kept as a
/// delegate (rather than LabelRenderer depending on AppDbContext/FileStorageService directly) so
/// the renderer itself stays a pure, easily testable function of its inputs.</summary>
public delegate byte[]? ImageResolver(int uploadedFileId);

/// <summary>
/// Renders a parsed label document to PNG or PDF using SkiaSharp. Both formats share the same
/// drawing code: every DrawXxx method works purely in the document's millimeter coordinate
/// space, and the caller picks the format by choosing the outer canvas scale (pixels-per-mm at
/// print resolution for PNG, points-per-mm — PDF's native unit — for PDF). This mirrors how the
/// browser editor scales its Konva layer, so both stay visually consistent by construction
/// rather than by coincidence.
/// </summary>
public sealed class LabelRenderer
{
    private const float PngDpi = 300f;
    private const float MmPerInch = 25.4f;
    private const float PxPerMmAt300Dpi = PngDpi / MmPerInch;
    private const float PtPerMm = 72f / MmPerInch;

    /// <summary>Font sizes are authored in points; converting to millimeters here lets the same
    /// drawing code work under either the PNG or PDF outer scale.</summary>
    private const float FontPtToMm = 0.352778f;

    public byte[] RenderPng(
        RenderableDocument document,
        IReadOnlyDictionary<string, string> fieldValues,
        ImageResolver resolveImage)
    {
        var width = Math.Max(1, (int)MathF.Ceiling((float)document.WidthMm * PxPerMmAt300Dpi));
        var height = Math.Max(1, (int)MathF.Ceiling((float)document.HeightMm * PxPerMmAt300Dpi));

        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale(PxPerMmAt300Dpi);
            DrawObjects(canvas, document, fieldValues, resolveImage);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public byte[] RenderPdf(
        RenderableDocument document,
        IReadOnlyDictionary<string, string> fieldValues,
        ImageResolver resolveImage)
    {
        using var stream = new MemoryStream();
        var widthPt = (float)document.WidthMm * PtPerMm;
        var heightPt = (float)document.HeightMm * PtPerMm;

        using (var pdfDocument = SKDocument.CreatePdf(stream))
        {
            using var canvas = pdfDocument.BeginPage(widthPt, heightPt);
            canvas.Scale(PtPerMm);
            DrawObjects(canvas, document, fieldValues, resolveImage);
            pdfDocument.EndPage();
            pdfDocument.Close();
        }

        return stream.ToArray();
    }

    private void DrawObjects(
        SKCanvas canvas,
        RenderableDocument document,
        IReadOnlyDictionary<string, string> fieldValues,
        ImageResolver resolveImage)
    {
        foreach (var obj in document.Objects)
        {
            if (obj.Hidden)
            {
                continue;
            }

            canvas.Save();
            canvas.Translate((float)obj.X, (float)obj.Y);

            if (obj.Rotation != 0)
            {
                canvas.RotateDegrees((float)obj.Rotation);
            }

            switch (obj)
            {
                case RenderableText text:
                    DrawText(canvas, text.Text, text.FontSize, text.FontFamily, text.FontWeight, text.Align, text.Fill, text.Width, text.Height, text.Fit);
                    break;

                case RenderableDynamicField field:
                    var value = fieldValues.TryGetValue(field.FieldName, out var v) ? v : string.Empty;
                    DrawText(canvas, value, field.FontSize, field.FontFamily, field.FontWeight, field.Align, field.Fill, field.Width, field.Height, field.Fit);
                    break;

                case RenderableRect rect:
                    DrawRect(canvas, rect);
                    break;

                case RenderableLine line:
                    DrawLine(canvas, line);
                    break;

                case RenderableImage image:
                    DrawImage(canvas, image, resolveImage);
                    break;
            }

            canvas.Restore();
        }
    }

    private static void DrawText(
        SKCanvas canvas,
        string text,
        decimal fontSizePt,
        string fontFamily,
        string fontWeight,
        string align,
        string fill,
        decimal widthMm,
        decimal heightMm,
        TextFitMode fit)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var fontSizeMm = (float)fontSizePt * FontPtToMm;

        if (fontSizeMm <= 0)
        {
            return;
        }

        using var typeface = SKTypeface.FromFamilyName(
            fontFamily,
            fontWeight == "bold" ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);

        // Measure at a fixed reference size and scale: glyph advances are linear in the size,
        // and it saves allocating a font per candidate size while fitting.
        const float referenceSize = 10f;
        using var measuringFont = new SKFont(typeface, referenceSize);
        var fitted = TextFitter.Fit(
            text, (float)widthMm, (float)heightMm, fontSizeMm, fit,
            (line, size) => measuringFont.MeasureText(line) * size / referenceSize);

        fontSizeMm = fitted.FontSize;
        using var font = new SKFont(typeface, fontSizeMm);

        if (fit != TextFitMode.None && widthMm > 0 && heightMm > 0)
        {
            // Last line of defence: a fixed-size box never paints outside itself, even if the
            // text could not be shrunk far enough (e.g. a single enormous word).
            canvas.ClipRect(SKRect.Create((float)widthMm, (float)heightMm));
        }

        using var paint = new SKPaint
        {
            Color = ColorParser.Parse(fill, SKColors.Black),
            IsAntialias = true
        };

        var textAlign = align switch
        {
            "center" => SKTextAlign.Center,
            "right" => SKTextAlign.Right,
            _ => SKTextAlign.Left
        };

        var originX = align switch
        {
            "center" => (float)widthMm / 2f,
            "right" => (float)widthMm,
            _ => 0f
        };

        var lineHeight = fontSizeMm * TextFitter.LineHeightFactor;
        var lines = fitted.Lines;

        for (var i = 0; i < lines.Count; i++)
        {
            // Approximate baseline: text sits roughly 0.8x its size below the top of its line.
            var baselineY = lineHeight * i + fontSizeMm * 0.8f;
            canvas.DrawText(lines[i], originX, baselineY, textAlign, font, paint);
        }
    }

    private static void DrawRect(SKCanvas canvas, RenderableRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var skRect = SKRect.Create((float)rect.Width, (float)rect.Height);
        var radius = (float)rect.CornerRadius;

        var fill = ColorParser.Parse(rect.Fill, SKColors.Transparent);

        if (fill.Alpha > 0)
        {
            using var fillPaint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(skRect, radius, radius, fillPaint);
        }

        if (rect.StrokeWidth > 0)
        {
            using var strokePaint = new SKPaint
            {
                Color = ColorParser.Parse(rect.Stroke, SKColors.Black),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)rect.StrokeWidth,
                IsAntialias = true
            };
            canvas.DrawRoundRect(skRect, radius, radius, strokePaint);
        }
    }

    private static void DrawLine(SKCanvas canvas, RenderableLine line)
    {
        if (line.StrokeWidth <= 0 || line.Points.Length < 4)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Color = ColorParser.Parse(line.Stroke, SKColors.Black),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)line.StrokeWidth,
            IsAntialias = true
        };

        canvas.DrawLine(
            (float)line.Points[0], (float)line.Points[1],
            (float)line.Points[2], (float)line.Points[3],
            paint);
    }

    private static void DrawImage(SKCanvas canvas, RenderableImage image, ImageResolver resolveImage)
    {
        if (image.Width <= 0 || image.Height <= 0)
        {
            return;
        }

        var bytes = resolveImage(image.UploadedFileId);

        if (bytes is null || bytes.Length == 0)
        {
            return;
        }

        var destRect = SKRect.Create((float)image.Width, (float)image.Height);

        if (LooksLikeSvg(bytes))
        {
            DrawSvg(canvas, bytes, destRect);
            return;
        }

        using var bitmap = SKBitmap.Decode(bytes);

        if (bitmap is not null)
        {
            canvas.DrawBitmap(bitmap, destRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }
    }

    private static void DrawSvg(SKCanvas canvas, byte[] bytes, SKRect destRect)
    {
        using var svg = new SKSvg();
        using var stream = new MemoryStream(bytes);
        svg.Load(stream);

        var picture = svg.Picture;

        if (picture is null || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
        {
            return;
        }

        canvas.Save();
        canvas.ClipRect(destRect);
        canvas.Translate(destRect.Left, destRect.Top);
        canvas.Scale(destRect.Width / picture.CullRect.Width, destRect.Height / picture.CullRect.Height);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    /// <summary>Cheap format sniff: SVG is XML text, every raster format Tapeory accepts starts
    /// with binary magic bytes.</summary>
    private static bool LooksLikeSvg(byte[] bytes)
    {
        var sample = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512));
        return sample.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }
}
