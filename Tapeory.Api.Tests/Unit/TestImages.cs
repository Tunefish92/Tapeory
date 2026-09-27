using BitMiracle.LibTiff.Classic;

namespace Tapeory.Api.Tests.Unit;

/// <summary>Small images in the formats P-touch Editor embeds, built in code.</summary>
internal static class TestImages
{
    /// <summary>A 24-bit BMP whose left half is black and right half white.</summary>
    public static byte[] Bmp(int width = 4, int height = 2)
    {
        var rowSize = (width * 3 + 3) & ~3;
        var pixelBytes = rowSize * height;
        var data = new byte[54 + pixelBytes];

        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BitConverter.GetBytes(data.Length).CopyTo(data, 2);
        BitConverter.GetBytes(54).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(width).CopyTo(data, 18);
        BitConverter.GetBytes(height).CopyTo(data, 22);
        BitConverter.GetBytes((short)1).CopyTo(data, 26);
        BitConverter.GetBytes((short)24).CopyTo(data, 28);
        BitConverter.GetBytes(pixelBytes).CopyTo(data, 34);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = x < width / 2 ? (byte)0 : (byte)255;
                var offset = 54 + y * rowSize + x * 3;
                data[offset] = data[offset + 1] = data[offset + 2] = value;
            }
        }

        return data;
    }

    /// <summary>A 1-bit, CCITT Group 4 compressed TIFF (as label artwork usually is) whose left
    /// half is black and right half white.</summary>
    public static byte[] BilevelTiff(int width = 16, int height = 4)
    {
        using var stream = new MemoryStream();

        using (var tiff = Tiff.ClientOpen("test", "w", stream, new TiffStream()))
        {
            tiff.SetField(TiffTag.IMAGEWIDTH, width);
            tiff.SetField(TiffTag.IMAGELENGTH, height);
            tiff.SetField(TiffTag.BITSPERSAMPLE, 1);
            tiff.SetField(TiffTag.SAMPLESPERPIXEL, 1);
            tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISWHITE);
            tiff.SetField(TiffTag.COMPRESSION, Compression.CCITTFAX4);
            tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
            tiff.SetField(TiffTag.ROWSPERSTRIP, height);

            var row = new byte[(width + 7) / 8];

            for (var x = 0; x < width / 2; x++)
            {
                row[x / 8] |= (byte)(0x80 >> (x % 8)); // MINISWHITE: a set bit is black
            }

            for (var y = 0; y < height; y++)
            {
                tiff.WriteScanline(row, y);
            }
        }

        return stream.ToArray();
    }
}
