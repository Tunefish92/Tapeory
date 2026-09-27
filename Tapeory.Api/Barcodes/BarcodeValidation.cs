using Tapeory.Api.Rendering;

namespace Tapeory.Api.Barcodes;

/// <summary>Checks that every barcode on a label can encode the value it would print, so a bad
/// value is reported instead of printing a crossed-out box.</summary>
public static class BarcodeValidation
{
    public static List<string> Validate(RenderableDocument document, IReadOnlyDictionary<string, string> fieldValues)
    {
        var errors = new List<string>();

        foreach (var barcode in document.Objects.OfType<RenderableBarcode>().Where(barcode => !barcode.Hidden))
        {
            var value = barcode.ValueFor(fieldValues);
            var result = BarcodeEncoder.Encode(barcode.Symbology, value);

            if (result.Error is { } error)
            {
                var source = barcode.FieldName.Length > 0 ? $"field \"{barcode.FieldName}\"" : "fixed value";
                errors.Add($"Barcode ({DisplayName(barcode.Symbology)}, {source}): {error}");
            }
        }

        return errors;
    }

    public static string DisplayName(string symbology) => symbology.ToLowerInvariant() switch
    {
        "code128" => "Code 128",
        "code39" => "Code 39",
        "ean13" => "EAN-13",
        "ean8" => "EAN-8",
        "upca" => "UPC-A",
        "upce" => "UPC-E",
        "itf" => "ITF",
        "codabar" => "Codabar",
        "qr" => "QR code",
        "datamatrix" => "Data Matrix",
        "pdf417" => "PDF417",
        "aztec" => "Aztec",
        _ => symbology
    };
}
