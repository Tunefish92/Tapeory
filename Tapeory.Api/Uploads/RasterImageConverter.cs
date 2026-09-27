using BitMiracle.LibTiff.Classic;
using SkiaSharp;

namespace Tapeory.Api.Uploads;

/// <summary>
/// Turns raster images the browser editor can't show (TIFF, BMP) into PNG. P-touch Editor stores
/// the images inside a .lbx as BMP or TIFF, and people have label artwork in those formats too.
/// TIFF goes through LibTiff.NET, since SkiaSharp has no TIFF decoder; everything else Skia can
/// decode (BMP, GIF, PNG, JPEG, WebP) goes through Skia.
/// </summary>
public static class RasterImageConverter
{
    /// <summary>Refuse to decode anything larger, so a crafted file can't claim a huge canvas
    /// and exhaust memory.</summary>
    private const long MaxPixels = 40_000_000;

    public static bool IsTiff(ReadOnlySpan<byte> data) =>
        data.Length >= 4
        && ((data[0] == 'I' && data[1] == 'I' && data[2] == 42 && data[3] == 0)
            || (data[0] == 'M' && data[1] == 'M' && data[2] == 0 && data[3] == 42));

    /// <summary>The image as PNG, or null if it isn't an image this can decode.</summary>
    public static byte[]? ToPng(byte[] data)
    {
        using var bitmap = IsTiff(data) ? DecodeTiff(data) : DecodeWithSkia(data);

        if (bitmap is null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png?.ToArray();
    }

    private static SKBitmap? DecodeWithSkia(byte[] data)
    {
        using var codec = SKCodec.Create(new MemoryStream(data));

        if (codec is null || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            return null;
        }

        return SKBitmap.Decode(codec);
    }

    private static SKBitmap? DecodeTiff(byte[] data)
    {
        // LibTiff reports problems through a static handler that writes to the console by
        // default; silence it and rely on the return values instead.
        Tiff.SetErrorHandler(new QuietTiffErrorHandler());

        using var tiff = Tiff.ClientOpen("image", "r", new MemoryStream(data), new TiffStream());

        if (tiff is null)
        {
            return null;
        }

        var width = tiff.GetField(TiffTag.IMAGEWIDTH)?[0].ToInt() ?? 0;
        var height = tiff.GetField(TiffTag.IMAGELENGTH)?[0].ToInt() ?? 0;

        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
        {
            return null;
        }

        // ReadRGBAImageOriented handles every photometric/compression combination (including the
        // 1-bit CCITT fax images label artwork often uses) and returns ABGR-packed pixels.
        var raster = new int[width * height];

        if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
        {
            return null;
        }

        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixels = new byte[raster.Length * 4];

        for (var i = 0; i < raster.Length; i++)
        {
            var abgr = raster[i];
            pixels[i * 4] = (byte)Tiff.GetR(abgr);
            pixels[i * 4 + 1] = (byte)Tiff.GetG(abgr);
            pixels[i * 4 + 2] = (byte)Tiff.GetB(abgr);
            pixels[i * 4 + 3] = (byte)Tiff.GetA(abgr);
        }

        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return bitmap;
    }

    private sealed class QuietTiffErrorHandler : TiffErrorHandler
    {
        public override void WarningHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
        {
        }

        public override void ErrorHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
        {
        }
    }
}
