using System.Text;

namespace Tapeory.Api.PrintData;

/// <summary>Turns a text file's bytes into text: UTF-8 or UTF-16 by their byte order mark, UTF-8
/// without one if the bytes are valid UTF-8, otherwise Windows-1252 (what Excel saves a CSV in
/// on a Western European Windows).</summary>
public static class TextFileDecoder
{
    static TextFileDecoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Decode(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes is [0xFF, 0xFE, ..])
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes is [0xFE, 0xFF, ..])
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }
}
