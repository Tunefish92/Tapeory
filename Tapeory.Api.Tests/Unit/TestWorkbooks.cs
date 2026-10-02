using System.IO.Compression;
using System.Security;
using System.Text;

namespace Tapeory.Api.Tests.Unit;

/// <summary>Builds small .xlsx files for tests. A cell is a string (text), a double (number), or
/// a (double, int) pair: a number with one of the styles below.</summary>
public static class TestWorkbooks
{
    /// <summary>Excel's short date, which follows the reader's region.</summary>
    public const int ShortDate = 1;

    /// <summary>The fixed date format dd.mm.yyyy.</summary>
    public const int GermanDate = 2;

    /// <summary>Two decimals: 0.00.</summary>
    public const int TwoDecimals = 3;

    public static byte[] Create(params (string Name, object?[][] Rows)[] sheets)
    {
        using var stream = new MemoryStream();

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }

            var sheetIds = Enumerable.Range(1, sheets.Length).ToList();

            Add("[Content_Types].xml",
                """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>"""
                + string.Concat(sheetIds.Select(id => $"""<Override PartName="/xl/worksheets/sheet{id}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>"""))
                + "</Types>");
            Add("_rels/.rels",
                """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add("xl/workbook.xml",
                """<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>"""
                + string.Concat(sheetIds.Select(id => $"""<sheet name="{SecurityElement.Escape(sheets[id - 1].Name)}" sheetId="{id}" r:id="rId{id}"/>"""))
                + "</sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels",
                """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
                + string.Concat(sheetIds.Select(id => $"""<Relationship Id="rId{id}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{id}.xml"/>"""))
                + $"""<Relationship Id="rId{sheets.Length + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            Add("xl/styles.xml",
                """<?xml version="1.0" encoding="UTF-8"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="dd\.mm\.yyyy"/></numFmts><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="1"><fill><patternFill patternType="none"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="4"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="2" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>""");

            foreach (var id in sheetIds)
            {
                var xml = new StringBuilder(
                    """<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
                var rows = sheets[id - 1].Rows;

                for (var r = 0; r < rows.Length; r++)
                {
                    xml.Append($"""<row r="{r + 1}">""");

                    for (var c = 0; c < rows[r].Length; c++)
                    {
                        var reference = $"{(char)('A' + c)}{r + 1}";
                        xml.Append(rows[r][c] switch
                        {
                            null => string.Empty,
                            string text => $"""<c r="{reference}" t="inlineStr"><is><t>{SecurityElement.Escape(text)}</t></is></c>""",
                            (double number, int style) => FormattableString.Invariant($"""<c r="{reference}" s="{style}"><v>{number}</v></c>"""),
                            var number => FormattableString.Invariant($"""<c r="{reference}"><v>{number}</v></c>""")
                        });
                    }

                    xml.Append("</row>");
                }

                xml.Append("</sheetData></worksheet>");
                Add($"xl/worksheets/sheet{id}.xml", xml.ToString());
            }
        }

        return stream.ToArray();
    }
}
