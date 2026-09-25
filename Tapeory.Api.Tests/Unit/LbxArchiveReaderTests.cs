using System.IO.Compression;
using System.Text;
using Tapeory.Api.Import;

namespace Tapeory.Api.Tests.Unit;

public sealed class LbxArchiveReaderTests
{
    private const string MinimalLabelXml =
        """<?xml version="1.0" encoding="UTF-8"?><pt:document xmlns:pt="http://schemas.brother.info/ptouch/2007/lbx/main"/>""";

    private const string MinimalPropXml =
        """<?xml version="1.0" encoding="UTF-8"?><meta:properties xmlns:meta="http://schemas.brother.info/ptouch/2007/lbx/meta" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>My Label</dc:title></meta:properties>""";

    private static MemoryStream BuildZip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Read_ParsesLabelXmlAndPropXml_WhenBothArePresent()
    {
        using var zip = BuildZip(("label.xml", MinimalLabelXml), ("prop.xml", MinimalPropXml));

        var result = LbxArchiveReader.Read(zip);

        Assert.NotNull(result.LabelXml.Root);
        Assert.Equal("document", result.LabelXml.Root!.Name.LocalName);
        Assert.NotNull(result.PropXml);
        Assert.Equal("My Label", result.PropXml!.Root!.Elements().First(e => e.Name.LocalName == "title").Value);
    }

    [Fact]
    public void Read_SucceedsWithoutPropXml_SinceItIsOptional()
    {
        using var zip = BuildZip(("label.xml", MinimalLabelXml));

        var result = LbxArchiveReader.Read(zip);

        Assert.NotNull(result.LabelXml.Root);
        Assert.Null(result.PropXml);
    }

    [Fact]
    public void Read_IsCaseInsensitiveAndDepthTolerant_AboutEntryNames()
    {
        using var zip = BuildZip(("Nested/Folder/Label.XML", MinimalLabelXml));

        var result = LbxArchiveReader.Read(zip);

        Assert.NotNull(result.LabelXml.Root);
    }

    [Fact]
    public void Read_Throws_WhenLabelXmlEntryIsMissing()
    {
        using var zip = BuildZip(("prop.xml", MinimalPropXml));

        var exception = Assert.Throws<LbxParseException>(() => LbxArchiveReader.Read(zip));
        Assert.Contains("label.xml", exception.Message);
    }

    [Fact]
    public void Read_Throws_WhenTheFileIsNotAZipArchiveAtAll()
    {
        using var notAZip = new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip file"));

        Assert.Throws<LbxParseException>(() => LbxArchiveReader.Read(notAZip));
    }

    [Fact]
    public void Read_Throws_WhenLabelXmlIsNotValidXml()
    {
        using var zip = BuildZip(("label.xml", "<not-valid-xml"));

        Assert.Throws<LbxParseException>(() => LbxArchiveReader.Read(zip));
    }
}
