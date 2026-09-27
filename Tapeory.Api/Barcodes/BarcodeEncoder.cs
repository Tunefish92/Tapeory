using ZXing;
using ZXing.Common;
using ZXing.QrCode.Internal;

namespace Tapeory.Api.Barcodes;

/// <summary>A barcode as a grid of modules: <see cref="Rows"/> × <see cref="Columns"/>, row by row,
/// true for a dark module. A 1D barcode has a single row, stretched to the object's height.</summary>
public sealed record BarcodeMatrix(int Columns, int Rows, bool[] Modules, bool IsTwoDimensional)
{
    public bool IsDark(int column, int row) => Modules[row * Columns + column];
}

public sealed record BarcodeResult(BarcodeMatrix? Matrix, string? Error)
{
    public static BarcodeResult Success(BarcodeMatrix matrix) => new(matrix, null);

    public static BarcodeResult Failure(string error) => new(null, error);
}

/// <summary>
/// Encodes barcodes and 2D codes with ZXing.Net. The editor draws the same module grid (fetched
/// from the API) as the renderer prints, so the preview matches the label exactly.
/// </summary>
public static class BarcodeEncoder
{
    /// <summary>Symbology names as stored in label documents.</summary>
    public static readonly IReadOnlyDictionary<string, BarcodeFormat> Symbologies =
        new Dictionary<string, BarcodeFormat>(StringComparer.OrdinalIgnoreCase)
        {
            ["code128"] = BarcodeFormat.CODE_128,
            ["code39"] = BarcodeFormat.CODE_39,
            ["ean13"] = BarcodeFormat.EAN_13,
            ["ean8"] = BarcodeFormat.EAN_8,
            ["upca"] = BarcodeFormat.UPC_A,
            ["upce"] = BarcodeFormat.UPC_E,
            ["itf"] = BarcodeFormat.ITF,
            ["codabar"] = BarcodeFormat.CODABAR,
            ["qr"] = BarcodeFormat.QR_CODE,
            ["datamatrix"] = BarcodeFormat.DATA_MATRIX,
            ["pdf417"] = BarcodeFormat.PDF_417,
            ["aztec"] = BarcodeFormat.AZTEC
        };

    private const int MaxDataLength = 2000;

    public static bool IsTwoDimensional(string symbology) =>
        Symbologies.TryGetValue(symbology, out var format)
        && format is BarcodeFormat.QR_CODE or BarcodeFormat.DATA_MATRIX or BarcodeFormat.PDF_417 or BarcodeFormat.AZTEC;

    public static BarcodeResult Encode(string symbology, string? data)
    {
        if (!Symbologies.TryGetValue(symbology, out var format))
        {
            return BarcodeResult.Failure($"Unknown barcode type '{symbology}'.");
        }

        if (string.IsNullOrEmpty(data))
        {
            return BarcodeResult.Failure("The barcode has no value.");
        }

        if (data.Length > MaxDataLength)
        {
            return BarcodeResult.Failure($"The barcode value is longer than {MaxDataLength} characters.");
        }

        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 0,
            [EncodeHintType.CHARACTER_SET] = "UTF-8"
        };

        if (format == BarcodeFormat.QR_CODE)
        {
            hints[EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M;
        }

        try
        {
            // Asking for a 1×1 size makes ZXing use its natural size: one pixel per module.
            var bits = new MultiFormatWriter().encode(data, format, 1, 1, hints);
            var twoDimensional = IsTwoDimensional(symbology);
            var rows = twoDimensional ? bits.Height : 1;
            var modules = new bool[bits.Width * rows];

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < bits.Width; column++)
                {
                    modules[row * bits.Width + column] = bits[column, row];
                }
            }

            return BarcodeResult.Success(new BarcodeMatrix(bits.Width, rows, modules, twoDimensional));
        }
        catch (Exception ex) when (ex is ArgumentException or WriterException or System.FormatException or InvalidOperationException)
        {
            return BarcodeResult.Failure(ex.Message);
        }
    }
}
