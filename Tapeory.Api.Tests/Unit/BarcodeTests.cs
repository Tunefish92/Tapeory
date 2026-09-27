using SkiaSharp;
using Tapeory.Api.Barcodes;
using Tapeory.Api.Rendering;

namespace Tapeory.Api.Tests.Unit;

public sealed class BarcodeTests
{
    [Theory]
    [InlineData("code128", "ABC-123", false)]
    [InlineData("code39", "ABC123", false)]
    [InlineData("ean13", "400638133393", false)]
    [InlineData("ean8", "9638507", false)]
    [InlineData("upca", "03600029145", false)]
    [InlineData("upce", "01234565", false)]
    [InlineData("itf", "12345678", false)]
    [InlineData("codabar", "A40156B", false)]
    [InlineData("qr", "https://tapeory.test", true)]
    [InlineData("datamatrix", "Hello", true)]
    [InlineData("pdf417", "Hello", true)]
    [InlineData("aztec", "Hello", true)]
    public void Encode_ProducesAModuleGrid_ForEverySymbology(string symbology, string data, bool twoDimensional)
    {
        var result = BarcodeEncoder.Encode(symbology, data);

        var matrix = Assert.IsType<BarcodeMatrix>(result.Matrix);
        Assert.Equal(twoDimensional, matrix.IsTwoDimensional);
        Assert.True(twoDimensional ? matrix.Rows > 1 : matrix.Rows == 1);
        Assert.Contains(true, matrix.Modules);
    }

    [Theory]
    [InlineData("ean13", "12345", "12 (without checksum digit) or 13 digits")]
    [InlineData("qr", "", "no value")]
    [InlineData("hologram", "x", "Unknown barcode type")]
    public void Encode_ExplainsWhyAValueCannotBeEncoded(string symbology, string data, string message)
    {
        var result = BarcodeEncoder.Encode(symbology, data);

        Assert.Null(result.Matrix);
        Assert.Contains(message, result.Error);
    }

    [Theory]
    [InlineData("ean13", "400638133393", "4006381333931")]
    [InlineData("ean13", "4006381333931", "4006381333931")]
    [InlineData("ean8", "9638507", "96385074")]
    [InlineData("upca", "03600029145", "036000291452")]
    [InlineData("code128", "ABC", "ABC")]
    public void DisplayText_AddsTheCheckDigit_WhenItWasLeftOut(string symbology, string value, string expected)
    {
        Assert.Equal(expected, BarcodeLayout.DisplayText(symbology, value));
    }

    private static RenderableDocument DocumentWith(RenderableObject barcode) => new(40m, 12m, [barcode]);

    private static RenderableBarcode Barcode(string symbology, string data, string fieldName = "", bool showText = false) =>
        new(1, 1, 0, false, false, 38, 10, symbology, data, fieldName, showText, "#000000");

    [Fact]
    public void Render_DrawsEveryBarAsAWholeNumberOfPrinterDots()
    {
        using var bitmap = new LabelRenderer().RenderBitmap(
            DocumentWith(Barcode("code128", "TAPEORY-42")), new Dictionary<string, string>(), _ => null, 180, 180);

        // Walk one row through the bars and collect the widths of the dark runs.
        var row = bitmap.Height / 2;
        var runs = new List<int>();
        var current = 0;

        for (var x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, row).Red < 128)
            {
                current++;
            }
            else if (current > 0)
            {
                runs.Add(current);
                current = 0;
            }
        }

        Assert.NotEmpty(runs);
        var module = runs.Min();
        Assert.All(runs, width => Assert.Equal(0, width % module)); // bars are 1–4 modules wide, exactly
        Assert.All(runs, width => Assert.InRange(width / module, 1, 4));
    }

    [Theory]
    [InlineData("code128", "TAPEORY-42", 38, 8)]
    [InlineData("ean13", "400638133393", 38, 10)]
    [InlineData("qr", "https://github.com/Tunefish92/Tapeory", 14, 14)]
    public void Render_PrintsBarcodesThatScan_At180Dpi(string symbology, string data, int widthMm, int heightMm)
    {
        var barcode = new RenderableBarcode(2, 2, 0, false, false, widthMm, heightMm, symbology, data, "", false, "#000000");
        var document = new RenderableDocument(widthMm + 4, heightMm + 4, [barcode]);

        using var bitmap = new LabelRenderer().RenderBitmap(document, new Dictionary<string, string>(), _ => null, 180, 180);

        // Threshold like the printer does, then decode what would come out of it.
        var pixels = new byte[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                pixels[y * bitmap.Width + x] = bitmap.GetPixel(x, y).Red < 128 ? (byte)0 : (byte)255;
            }
        }

        var source = new ZXing.RGBLuminanceSource(pixels, bitmap.Width, bitmap.Height, ZXing.RGBLuminanceSource.BitmapFormat.Gray8);
        var reader = new ZXing.BarcodeReaderGeneric { Options = { TryHarder = true } };
        var decoded = reader.Decode(source);

        Assert.NotNull(decoded);
        Assert.Equal(BarcodeLayout.DisplayText(symbology, data), decoded.Text);
    }

    [Fact]
    public void Render_UsesTheFieldValue_AndDrawsAPlaceholder_WhenTheValueCannotBeEncoded()
    {
        var barcode = Barcode("ean13", "400638133393", fieldName: "gtin");
        var renderer = new LabelRenderer();

        using var valid = renderer.RenderBitmap(DocumentWith(barcode), new Dictionary<string, string>(), _ => null, 180, 180);
        using var invalid = renderer.RenderBitmap(
            DocumentWith(barcode), new Dictionary<string, string> { ["gtin"] = "not-a-number" }, _ => null, 180, 180);

        static int DarkPixels(SKBitmap bitmap) =>
            Enumerable.Range(0, bitmap.Width).Sum(x => Enumerable.Range(0, bitmap.Height).Count(y => bitmap.GetPixel(x, y).Red < 128));

        Assert.True(DarkPixels(valid) > DarkPixels(invalid) * 2); // bars vs. a thin grey outline and cross
    }

    [Fact]
    public void Validation_ReportsBarcodesWhoseValueCannotBeEncoded()
    {
        var document = new RenderableDocument(40m, 12m,
        [
            Barcode("ean13", "400638133393", fieldName: "gtin"),
            Barcode("qr", "fine")
        ]);

        Assert.Empty(BarcodeValidation.Validate(document, new Dictionary<string, string>()));

        var error = Assert.Single(BarcodeValidation.Validate(document, new Dictionary<string, string> { ["gtin"] = "12" }));
        Assert.Contains("EAN-13", error);
        Assert.Contains("gtin", error);
    }

    [Fact]
    public void Parser_ReadsBarcodesAndEllipses()
    {
        var document = LabelDocumentParser.Parse("""
            {"widthMm":40,"heightMm":12,"objects":[
              {"type":"barcode","x":1,"y":2,"width":30,"height":8,"rotation":0,"symbology":"qr","data":"hi","fieldName":"sku","showText":true,"fill":"#000000"},
              {"type":"ellipse","x":3,"y":4,"width":5,"height":6,"rotation":0,"fill":"#ff0000","stroke":"#000000","strokeWidth":0.3}
            ]}
            """);

        var barcode = Assert.IsType<RenderableBarcode>(document.Objects[0]);
        Assert.Equal(("qr", "hi", "sku", true), (barcode.Symbology, barcode.Data, barcode.FieldName, barcode.ShowText));
        var ellipse = Assert.IsType<RenderableEllipse>(document.Objects[1]);
        Assert.Equal((5m, 6m, "#ff0000"), (ellipse.Width, ellipse.Height, ellipse.Fill));
    }
}
