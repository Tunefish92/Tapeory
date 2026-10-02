using System.Text;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.PrintData;

namespace Tapeory.Api.Tests.Unit;

public sealed class PrintDataTests
{
    private static TemplateField Field(string name, string? label = null, bool required = true) =>
        new() { Name = name, Label = label, Required = required };

    private static readonly TemplateField[] NameAndSku = [Field("name", "Name"), Field("sku", "Article no.")];

    private static PrintDataResponse Read(string text, string? separator = null, TemplateField[]? fields = null) =>
        Read(Encoding.UTF8.GetBytes(text), separator, fields: fields);

    private static PrintDataResponse Read(byte[] bytes, string? separator = null, int? sheet = null, string locale = "en-US", TemplateField[]? fields = null)
    {
        var data = PrintDataReader.Read(bytes, separator, sheet, DataLocale.For(locale), fields ?? NameAndSku, out var error);
        Assert.Null(error);
        return data!;
    }

    // --- Reading delimited text ---

    [Fact]
    public void Reader_SplitsRowsAndValues()
    {
        var rows = DelimitedTextReader.Read("a;b;c\r\n1;2;3\n4;5;6", ';');

        Assert.Equal([["a", "b", "c"], ["1", "2", "3"], ["4", "5", "6"]], rows);
    }

    [Fact]
    public void Reader_KeepsSeparatorsLineBreaksAndQuotes_InsideQuotedValues()
    {
        var rows = DelimitedTextReader.Read("name,note\n\"Smith, Anna\",\"said \"\"hi\"\"\nand left\"\n", ',');

        Assert.Equal([["name", "note"], ["Smith, Anna", "said \"hi\"\nand left"]], rows);
    }

    [Fact]
    public void Reader_SkipsEmptyLines_AndTrimsValues()
    {
        var rows = DelimitedTextReader.Read("\n a ; b \n\n ; \n1;2\n", ';');

        Assert.Equal([["a", "b"], ["1", "2"]], rows);
    }

    [Fact]
    public void Reader_KeepsEmptyValues()
    {
        Assert.Equal([["a", "", "c", ""]], DelimitedTextReader.Read("a,,c,", ','));
    }

    [Fact]
    public void Reader_TakesEveryLineAsOneValue_WithoutASeparator()
    {
        var rows = DelimitedTextReader.Read("Screws, M4\n\"Quoted\"\nNuts", null);

        Assert.Equal([["Screws, M4"], ["\"Quoted\""], ["Nuts"]], rows);
    }

    [Fact]
    public void Reader_StopsAtTheRowLimit()
    {
        Assert.Equal(2, DelimitedTextReader.Read("1\n2\n3\n4", null, maxRows: 2).Count);
    }

    // --- Detecting the separator ---

    [Theory]
    [InlineData("name,sku\nBox,1\nBag,2", ',')]
    [InlineData("name;sku\nBox;1\nBag;2", ';')]
    [InlineData("name\tsku\nBox\t1\nBag\t2", '\t')]
    [InlineData("name|sku\nBox|1\nBag|2", '|')]
    public void Separator_IsDetected(string text, char expected)
    {
        Assert.Equal(new SeparatorGuess(expected, true), SeparatorDetector.Detect(text));
    }

    [Fact]
    public void Separator_IsTheSemicolon_WhenValuesHaveDecimalCommas()
    {
        Assert.Equal(new SeparatorGuess(';', true), SeparatorDetector.Detect("name;price\nBox;1,50\nBag;12,00"));
    }

    [Fact]
    public void Separator_IsTheComma_WhenAQuotedValueHoldsASemicolon()
    {
        Assert.Equal(new SeparatorGuess(',', true), SeparatorDetector.Detect("name,note\nBox,\"a; b\"\nBag,c"));
    }

    [Fact]
    public void Separator_IsNone_ForOneValuePerLine()
    {
        Assert.Equal(new SeparatorGuess(null, true), SeparatorDetector.Detect("Box\nBag\nCrate"));
    }

    [Fact]
    public void Separator_IsNotDetected_WhenTwoFitEquallyWell()
    {
        var guess = SeparatorDetector.Detect("Smith, Anna;12\nJones, Ben;7");

        Assert.False(guess.Detected);
    }

    [Fact]
    public void Separator_IsNotDetected_WhenLinesAreUneven()
    {
        var guess = SeparatorDetector.Detect("Screws, M4\nNuts\nBolts, M6, long");

        Assert.Equal(new SeparatorGuess(null, false), guess);
    }

    // --- Decoding ---

    [Fact]
    public void Decoder_ReadsUtf8_WithAndWithoutByteOrderMark()
    {
        var plain = Encoding.UTF8.GetBytes("Größe");

        Assert.Equal("Größe", TextFileDecoder.Decode(plain));
        Assert.Equal("Größe", TextFileDecoder.Decode([0xEF, 0xBB, 0xBF, .. plain]));
    }

    [Fact]
    public void Decoder_ReadsUtf16()
    {
        Assert.Equal("Größe", TextFileDecoder.Decode([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("Größe")]));
    }

    [Fact]
    public void Decoder_FallsBackToWindows1252()
    {
        // "Größe;5 €" as German Excel saves it.
        Assert.Equal("Größe;5 €", TextFileDecoder.Decode([0x47, 0x72, 0xF6, 0xDF, 0x65, 0x3B, 0x35, 0x20, 0x80]));
    }

    // --- Matching columns to fields ---

    [Fact]
    public void Match_FindsTheHeader_ByNameAndLabel_IgnoringCaseAccentsAndSpaces()
    {
        var fields = new[] { Field("name"), Field("size", "Größe"), Field("note", required: false) };

        var match = ColumnMatcher.Match([["  GROSSE ", " große", "NAME "], ["1", "2", "Box"]], fields);

        Assert.True(match.HasHeader);
        Assert.Equal(new Dictionary<string, int> { ["name"] = 2, ["size"] = 1 }, match.Fields);
        Assert.Equal("stuck-nr. 1", ColumnMatcher.Normalize("  Stück-Nr.   1 "));
    }

    [Fact]
    public void Match_PrefersAFieldsNameOverAnotherFieldsLabel()
    {
        var fields = new[] { Field("title", "Name"), Field("name", "Full name") };

        var match = ColumnMatcher.Match([["name", "title"], ["a", "b"]], fields);

        Assert.Equal(new Dictionary<string, int> { ["name"] = 0, ["title"] = 1 }, match.Fields);
    }

    [Fact]
    public void Match_FindsTheQuantityColumn()
    {
        var match = ColumnMatcher.Match([["Name", "Anzahl"], ["Box", "3"]], NameAndSku);

        Assert.Equal(1, match.QuantityColumn);
        Assert.Equal(new Dictionary<string, int> { ["name"] = 0 }, match.Fields);
    }

    [Fact]
    public void Match_SeesNoHeader_WhenNoCellNamesAField()
    {
        var match = ColumnMatcher.Match([["Box", "A-1"], ["Bag", "A-2"]], NameAndSku);

        Assert.False(match.HasHeader);
        Assert.Empty(match.Fields);
    }

    [Fact]
    public void Match_FillsTheOnlyField_FromTheOnlyColumn()
    {
        var match = ColumnMatcher.Match([["Box"], ["Bag"]], [Field("name")]);

        Assert.False(match.HasHeader);
        Assert.Equal(new Dictionary<string, int> { ["name"] = 0 }, match.Fields);
    }

    // --- Whole files ---

    [Fact]
    public void Read_ACsvFile_WithHeader()
    {
        var data = Read("Name;Article no.;Qty\nBox;A-1;2\nBag;A-2;1\n");

        Assert.Equal(("text", ";", true, true), (data.Kind, data.Separator, data.SeparatorDetected, data.HasHeader));
        Assert.Equal(3, data.Rows.Count);
        Assert.Equal(new Dictionary<string, int> { ["name"] = 0, ["sku"] = 1 }, data.Fields);
        Assert.Equal(2, data.QuantityColumn);
    }

    [Fact]
    public void Read_UsesTheSeparatorTheUserChose()
    {
        var text = "Smith, Anna;12\nJones, Ben;7";

        Assert.False(Read(text).SeparatorDetected);
        Assert.Equal([["Smith, Anna", "12"], ["Jones, Ben", "7"]], Read(text, ";").Rows);
        Assert.Equal([["Smith", "Anna;12"], ["Jones", "Ben;7"]], Read(text, ",").Rows);
        Assert.Equal("tab", Read("a\tb\nc\td", "tab").Separator);
        Assert.Equal([["a b"], ["c"]], Read("a b\nc", "none").Rows);
        Assert.Equal([["a", "b"], ["c", ""]], Read("a b\nc", "space").Rows);
    }

    [Fact]
    public void Read_FillsUpShortRows()
    {
        Assert.Equal([["a", "b", "c"], ["1", "", ""]], Read("a,b,c\n1", ",").Rows);
    }

    [Theory]
    [InlineData("", "no data")]
    [InlineData("\n \n", "no data")]
    public void Read_RefusesAnEmptyFile(string text, string expected)
    {
        Assert.Null(PrintDataReader.Read(Encoding.UTF8.GetBytes(text), null, null, DataLocale.For("en"), NameAndSku, out var error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void Read_RefusesABinaryFile_AndABadSeparator()
    {
        Assert.Null(PrintDataReader.Read([0x89, 0x50, 0x4E, 0x47, 0, 0, 1, 2], null, null, DataLocale.For("en"), NameAndSku, out var binary));
        Assert.Null(PrintDataReader.Read("a,b"u8.ToArray(), "ab", null, DataLocale.For("en"), NameAndSku, out var separator));

        Assert.Contains("isn't a text file", binary);
        Assert.Contains("single character", separator);
    }

    [Fact]
    public void Read_RefusesMoreRowsThanAJobTakes_ButNotTheHeaderOnTop()
    {
        var rows = string.Join('\n', Enumerable.Range(1, PrintDataReader.MaxRows).Select(i => $"Item {i};{i}"));

        Assert.Equal(PrintDataReader.MaxRows + 1, Read("name;sku\n" + rows).Rows.Count);
        Assert.Null(PrintDataReader.Read(Encoding.UTF8.GetBytes("name;sku\n" + rows + "\nOne more;x"), null, null, DataLocale.For("en"), NameAndSku, out var error));
        Assert.Contains($"more than {PrintDataReader.MaxRows} rows", error);
    }

    // --- Excel ---

    [Fact]
    public void Read_AnExcelFile_WithNumbersAndDatesAsExcelShowsThem()
    {
        // 46294 = 29 September 2026.
        var workbook = TestWorkbooks.Create(("Stock", [
            ["Name", "sku", "Price", "Since", "Until", "Count"],
            ["Box", "A-1", (1.5, TestWorkbooks.TwoDecimals), (46294d, TestWorkbooks.ShortDate), (46294d, TestWorkbooks.GermanDate), 12d],
            ["Bag", null, 0.25, null, null, 1234567d],
        ]));

        var german = Read(workbook, locale: "de-DE");
        var american = Read(workbook, locale: "en-US");

        Assert.Equal(("spreadsheet", null, true), (german.Kind, german.Separator, german.HasHeader));
        Assert.Equal(["Stock"], german.Sheets);
        Assert.Equal(["Box", "A-1", "1,50", "29.09.2026", "29.09.2026", "12"], german.Rows[1]);
        Assert.Equal(["Bag", "", "0,25", "", "", "1234567"], german.Rows[2]);
        Assert.Equal(["Box", "A-1", "1.50", "9/29/2026", "29.09.2026", "12"], american.Rows[1]);
        Assert.Equal(new Dictionary<string, int> { ["name"] = 0, ["sku"] = 1 }, german.Fields);
    }

    [Fact]
    public void Read_TakesTheFirstSheetWithData_OrTheOneAskedFor()
    {
        var workbook = TestWorkbooks.Create(
            ("Empty", []),
            ("First", [["name"], ["Box"]]),
            ("Second", [["name"], ["Bag"], ["Crate"]]));

        var automatic = Read(workbook);
        var second = Read(workbook, sheet: 2);

        Assert.Equal(["Empty", "First", "Second"], automatic.Sheets);
        Assert.Equal((1, 2), (automatic.Sheet, automatic.Rows.Count));
        Assert.Equal((2, 3), (second.Sheet, second.Rows.Count));
    }

    [Fact]
    public void Read_RefusesABrokenExcelFile()
    {
        Assert.Null(PrintDataReader.Read([0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5], null, null, DataLocale.For("en"), NameAndSku, out var error));
        Assert.Contains("can't be read", error);
    }

    // --- JSON ---

    [Fact]
    public void Read_AJsonListOfRecords_WithThePropertyNamesAsHeader()
    {
        var data = Read("""
            [
              { "sku": "A-1", "name": "Box", "qty": 2, "fragile": true },
              { "name": "Bag", "sku": "A-2", "note": null, "extra": "x" }
            ]
            """);

        Assert.Equal(("json", null, true, true), (data.Kind, data.Separator, data.SeparatorDetected, data.HasHeader));
        Assert.Equal(["sku", "name", "qty", "fragile", "note", "extra"], data.Rows[0]);
        Assert.Equal(["A-1", "Box", "2", "true", "", ""], data.Rows[1]);
        Assert.Equal(["A-2", "Bag", "", "", "", "x"], data.Rows[2]);
        Assert.Equal(new Dictionary<string, int> { ["name"] = 1, ["sku"] = 0 }, data.Fields);
        Assert.Equal(2, data.QuantityColumn);
    }

    [Fact]
    public void Read_FindsTheListInsideAJsonDocument_AndFlattensNestedObjects()
    {
        var data = Read("""
            { "total": 2, "data": { "items": [
                { "name": "Box", "place": { "shelf": "A", "level": 3 }, "tags": ["x", "y"] },
                { "name": "Bag", "place": { "shelf": "B" } }
            ] } }
            """);

        Assert.Equal(["name", "place.shelf", "place.level", "tags"], data.Rows[0]);
        Assert.Equal(["Box", "A", "3", """["x", "y"]"""], data.Rows[1]);
        Assert.Equal(["Bag", "B", "", ""], data.Rows[2]);
    }

    [Fact]
    public void Read_KeepsTheJsonHeader_EvenWhenNoPropertyNamesAField()
    {
        var data = Read("""[{ "title": "Box" }, { "title": "Bag" }]""");

        Assert.True(data.HasHeader);
        Assert.Empty(data.Fields);
        Assert.Equal(3, data.Rows.Count);
    }

    [Fact]
    public void Read_AJsonListOfPlainValues_AsOneColumn()
    {
        var data = Read("""["Box", "Bag", 3]""", fields: [Field("value")]);

        Assert.Equal([["value"], ["Box"], ["Bag"], ["3"]], data.Rows);
        Assert.Equal(new Dictionary<string, int> { ["value"] = 0 }, data.Fields);
    }

    [Theory]
    [InlineData("""{ "name": "Box" }""", "no list of records")]
    [InlineData("[]", "no data")]
    public void Read_RefusesJsonWithoutRecords(string json, string expected)
    {
        Assert.Null(PrintDataReader.Read(Encoding.UTF8.GetBytes(json), null, null, DataLocale.For("en"), NameAndSku, out var error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void Read_TakesTextThatOnlyLooksLikeJson_AsText()
    {
        var data = Read("[A] Box;1\n[B] Bag;2");

        Assert.Equal(("text", ";"), (data.Kind, data.Separator));
        Assert.Equal(["[A] Box", "1"], data.Rows[0]);
    }

    [Fact]
    public void Read_RefusesMoreJsonRecordsThanAJobTakes()
    {
        var json = "[" + string.Join(',', Enumerable.Range(0, PrintDataReader.MaxRows + 1).Select(i => $"{{\"name\":\"Item {i}\"}}")) + "]";

        Assert.Null(PrintDataReader.Read(Encoding.UTF8.GetBytes(json), null, null, DataLocale.For("en"), NameAndSku, out var error));
        Assert.Contains($"more than {PrintDataReader.MaxRows} rows", error);
    }
}
