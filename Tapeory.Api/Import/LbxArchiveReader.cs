using System.IO.Compression;
using System.Xml.Linq;

namespace Tapeory.Api.Import;

public sealed class LbxParseException(string message) : Exception(message);

/// <param name="Files">The archive's other entries (embedded images) by file name, ignoring case.</param>
public sealed record LbxArchiveContents(
    XDocument LabelXml, XDocument? PropXml, IReadOnlyDictionary<string, byte[]> Files);

/// <summary>Opens a .lbx file (a ZIP archive) and locates its label.xml / prop.xml entries.
/// Brother's own tooling puts these at the archive root, but this matches by file name at any
/// depth to tolerate archives built differently.</summary>
public static class LbxArchiveReader
{
    public static LbxArchiveContents Read(Stream zipStream)
    {
        ZipArchive archive;

        try
        {
            archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new LbxParseException("The file is not a valid .lbx (zip) archive.");
        }

        using (archive)
        {
            var labelEntry = FindEntry(archive, "label.xml")
                ?? throw new LbxParseException("The archive does not contain a label.xml entry.");

            var labelXml = LoadXml(labelEntry, "label.xml");

            var propEntry = FindEntry(archive, "prop.xml");
            var propXml = propEntry is null ? null : LoadXml(propEntry, "prop.xml");

            return new LbxArchiveContents(labelXml, propXml, ReadFiles(archive));
        }
    }

    /// <summary>Caps what one archive may unpack, so a zip bomb can't fill memory.</summary>
    private const long MaxEmbeddedBytes = 50 * 1024 * 1024;

    private static Dictionary<string, byte[]> ReadFiles(ZipArchive archive)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        foreach (var entry in archive.Entries)
        {
            if (entry.Name.Length == 0 || entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Counts the bytes actually unpacked: an entry's declared size can lie.
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;

            while ((read = stream.Read(chunk)) > 0)
            {
                total += read;

                if (total > MaxEmbeddedBytes)
                {
                    return files;
                }

                buffer.Write(chunk, 0, read);
            }

            files[entry.Name] = buffer.ToArray();
        }

        return files;
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string fileName) =>
        archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase));

    private static XDocument LoadXml(ZipArchiveEntry entry, string label)
    {
        try
        {
            using var stream = entry.Open();
            return XDocument.Load(stream);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidDataException)
        {
            throw new LbxParseException($"{label} could not be parsed as XML.");
        }
    }
}
