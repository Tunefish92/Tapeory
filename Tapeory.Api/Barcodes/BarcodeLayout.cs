namespace Tapeory.Api.Barcodes;

/// <summary>Layout rules the renderer and the browser editor share (the editor mirrors these
/// numbers), so a barcode looks the same in the preview and on the label.</summary>
public static class BarcodeLayout
{
    /// <summary>Share of a 1D barcode's height used by its text line, capped at 4 mm.</summary>
    public static float TextHeight(float barcodeHeight) => MathF.Min(barcodeHeight * 0.25f, 4f);

    /// <summary>The font size, as a share of the text line's height.</summary>
    public const float TextSizeRatio = 0.8f;

    /// <summary>The value as printed under the bars: EAN/UPC values entered without their check
    /// digit get it appended, as the encoder adds it to the bars.</summary>
    public static string DisplayText(string symbology, string value)
    {
        var expected = symbology.ToLowerInvariant() switch
        {
            "ean13" => 12,
            "ean8" => 7,
            "upca" => 11,
            _ => 0
        };

        return expected > 0 && value.Length == expected && value.All(char.IsAsciiDigit)
            ? value + CheckDigit(value)
            : value;
    }

    /// <summary>The GS1 check digit: weights 3 and 1 alternating from the right.</summary>
    private static int CheckDigit(string digits)
    {
        var sum = 0;

        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i] - '0';
            sum += i % 2 == 0 ? digit * 3 : digit;
        }

        return (10 - sum % 10) % 10;
    }
}
