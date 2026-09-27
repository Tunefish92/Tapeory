using SkiaSharp;
using Tapeory.Api.Barcodes;
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
    private const float PtPerMm = 72f / MmPerInch;

    /// <summary>Font sizes are authored in points; converting to millimeters here lets the same
    /// drawing code work under either the PNG or PDF outer scale.</summary>
    private const float FontPtToMm = 0.352778f;

    public byte[] RenderPng(
        RenderableDocument document,
        IReadOnlyDictionary<string, string> fieldValues,
        ImageResolver resolveImage)
    {
        using var bitmap = RenderBitmap(document, fieldValues, resolveImage, PngDpi, PngDpi);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Renders onto a white bitmap at the given resolution, which may differ per axis.
    /// The caller owns (and disposes) the result; printer drivers use this to render at the print
    /// head's own resolution.</summary>
    public SKBitmap RenderBitmap(
        RenderableDocument document,
        IReadOnlyDictionary<string, string> fieldValues,
        ImageResolver resolveImage,
        float horizontalDpi,
        float verticalDpi)
    {
        var pxPerMmX = horizontalDpi / MmPerInch;
        var pxPerMmY = verticalDpi / MmPerInch;
        var width = Math.Max(1, (int)MathF.Ceiling((float)document.WidthMm * pxPerMmX));
        var height = Math.Max(1, (int)MathF.Ceiling((float)document.HeightMm * pxPerMmY));

        var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale(pxPerMmX, pxPerMmY);
            DrawObjects(canvas, document, fieldValues, resolveImage, snapToPixels: true);
        }

        return bitmap;
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
            DrawObjects(canvas, document, fieldValues, resolveImage, snapToPixels: false);
            pdfDocument.EndPage();
            pdfDocument.Close();
        }

        return stream.ToArray();
    }

    private void DrawObjects(
        SKCanvas canvas,
        RenderableDocument document,
        IReadOnlyDictionary<string, string> fieldValues,
        ImageResolver resolveImage,
        bool snapToPixels)
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

                case RenderableEllipse ellipse:
                    DrawEllipse(canvas, ellipse);
                    break;

                case RenderableBarcode barcode:
                    DrawBarcode(canvas, barcode, barcode.ValueFor(fieldValues), snapToPixels);
                    break;
            }

            canvas.Restore();
        }
    }

    /// <summary>
    /// Sizes are in millimetres on a canvas scaled up to the output resolution, so a font is often
    /// only a few units tall. Hinting would snap each glyph's advance to whole units at that size,
    /// and the scale-up turns the rounding into visibly uneven letter spacing; unhinted, linear
    /// metrics keep the spacing true at any size.
    /// </summary>
    private static SKFont CreateFont(SKTypeface typeface, float size) => new(typeface, size)
    {
        Hinting = SKFontHinting.None,
        Subpixel = true,
        LinearMetrics = true
    };

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

        using var typeface = FontResolver.Typeface(fontFamily, fontWeight == "bold");

        // Measure at a fixed reference size and scale: glyph advances are linear in the size,
        // and it saves allocating a font per candidate size while fitting.
        const float referenceSize = 10f;
        using var measuringFont = CreateFont(typeface, referenceSize);
        var fitted = TextFitter.Fit(
            text, (float)widthMm, (float)heightMm, fontSizeMm, fit,
            (line, size) => measuringFont.MeasureText(line) * size / referenceSize);

        fontSizeMm = fitted.FontSize;
        using var font = CreateFont(typeface, fontSizeMm);

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

    private static void DrawEllipse(SKCanvas canvas, RenderableEllipse ellipse)
    {
        if (ellipse.Width <= 0 || ellipse.Height <= 0)
        {
            return;
        }

        var bounds = SKRect.Create((float)ellipse.Width, (float)ellipse.Height);
        var fill = ColorParser.Parse(ellipse.Fill, SKColors.Transparent);

        if (fill.Alpha > 0)
        {
            using var fillPaint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawOval(bounds, fillPaint);
        }

        if (ellipse.StrokeWidth > 0)
        {
            using var strokePaint = new SKPaint
            {
                Color = ColorParser.Parse(ellipse.Stroke, SKColors.Black),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)ellipse.StrokeWidth,
                IsAntialias = true
            };
            canvas.DrawOval(bounds, strokePaint);
        }
    }

    /// <summary>
    /// Draws the barcode's modules into its box: 1D bars span the height (less the text line when
    /// shown), 2D modules stay square and are centred. Each module is snapped to a whole number of
    /// output pixels and drawn without anti-aliasing, so bars keep exactly equal widths at the
    /// printer's 180 dpi — uneven bars are what makes low-resolution barcodes unreadable (PDF output,
    /// which has no pixels, keeps the exact size instead). A value
    /// the symbology can't encode draws a crossed-out box instead.
    /// </summary>
    private static void DrawBarcode(SKCanvas canvas, RenderableBarcode barcode, string value, bool snapToPixels)
    {
        var width = (float)barcode.Width;
        var height = (float)barcode.Height;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var result = BarcodeEncoder.Encode(barcode.Symbology, value);

        if (result.Matrix is not { } matrix)
        {
            DrawBarcodePlaceholder(canvas, width, height);
            return;
        }

        var matrixTransform = canvas.TotalMatrix;
        var pxPerUnit = MathF.Sqrt(matrixTransform.ScaleX * matrixTransform.ScaleX + matrixTransform.SkewY * matrixTransform.SkewY);
        var textHeight = !matrix.IsTwoDimensional && barcode.ShowText ? BarcodeLayout.TextHeight(height) : 0f;
        var barsHeight = height - textHeight;

        var fitColumns = width / matrix.Columns;
        var fitRows = matrix.IsTwoDimensional ? barsHeight / matrix.Rows : float.MaxValue;
        var fit = MathF.Min(fitColumns, fitRows);
        var module = snapToPixels ? Snap(fit, pxPerUnit) : fit;
        var moduleHeight = matrix.IsTwoDimensional ? module : barsHeight;
        var left = (width - module * matrix.Columns) / 2f;
        var top = matrix.IsTwoDimensional ? (barsHeight - module * matrix.Rows) / 2f : 0f;

        using var paint = new SKPaint
        {
            Color = ColorParser.Parse(barcode.Fill, SKColors.Black),
            Style = SKPaintStyle.Fill,
            IsAntialias = !snapToPixels
        };

        for (var row = 0; row < matrix.Rows; row++)
        {
            var column = 0;

            while (column < matrix.Columns)
            {
                if (!matrix.IsDark(column, row))
                {
                    column++;
                    continue;
                }

                var runStart = column;
                while (column < matrix.Columns && matrix.IsDark(column, row))
                {
                    column++;
                }

                canvas.DrawRect(
                    SKRect.Create(left + runStart * module, top + row * moduleHeight, (column - runStart) * module, moduleHeight),
                    paint);
            }
        }

        if (textHeight > 0)
        {
            DrawBarcodeText(canvas, BarcodeLayout.DisplayText(barcode.Symbology, value), width, barsHeight, textHeight, barcode.Fill);
        }
    }

    /// <summary>The largest whole number of output pixels that fits, in document units; below
    /// one pixel per module there's nothing to snap to.</summary>
    private static float Snap(float size, float pxPerUnit)
    {
        var pixels = MathF.Floor(size * pxPerUnit);
        return pixels >= 1 ? pixels / pxPerUnit : size;
    }

    private static void DrawBarcodeText(SKCanvas canvas, string text, float width, float top, float height, string fill)
    {
        using var typeface = FontResolver.Typeface("Arial", bold: false);
        var fontSize = height * BarcodeLayout.TextSizeRatio;
        using var font = CreateFont(typeface, fontSize);
        var measured = font.MeasureText(text);

        if (measured > width && measured > 0)
        {
            font.Size = fontSize * width / measured;
        }

        using var paint = new SKPaint { Color = ColorParser.Parse(fill, SKColors.Black), IsAntialias = true };
        canvas.DrawText(text, width / 2f, top + height * 0.85f, SKTextAlign.Center, font, paint);
    }

    private static void DrawBarcodePlaceholder(SKCanvas canvas, float width, float height)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(0x99, 0x99, 0x99),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Min(width, height) * 0.03f,
            IsAntialias = true
        };
        canvas.DrawRect(SKRect.Create(width, height), paint);
        canvas.DrawLine(0, 0, width, height, paint);
        canvas.DrawLine(width, 0, 0, height, paint);
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
