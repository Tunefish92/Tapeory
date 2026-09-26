using System.IO.Compression;
using System.Xml.Linq;

namespace Tapeory.Api.Import;

public sealed class LbxParseException(string message) : Exception(message);

public sealed record LbxArchiveContents(XDocument LabelXml, XDocument? PropXml);

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

            return new LbxArchiveContents(labelXml, propXml);
        }
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
