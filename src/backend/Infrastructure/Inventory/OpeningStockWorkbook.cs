using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Domain.Catalog;
using FluentValidation;
using FluentValidation.Results;

namespace Infrastructure.Inventory;

/// <summary>Reads and writes the single-sheet opening stock template in Excel's XLSX format.</summary>
internal static class OpeningStockWorkbook
{
    internal sealed record ItemRow(string Sku, string Name, bool HasHistory);

    private const string SheetPath = "xl/worksheets/sheet1.xml";
    private const int MaxSheetRows = 1_048_576;
    private const int MaxXmlBytes = 32 * 1024 * 1024;
    private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly string[] Headers = ["SKU", "Item", "Opening quantity", "Unit cost", "Selling price (optional)", "Opening stock status"];

    internal static byte[] Write(IReadOnlyList<ItemRow> items, CancellationToken ct)
    {
        if (items.Count >= MaxSheetRows) throw Invalid("There are too many items for one Excel worksheet.");
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePart(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            WritePart(archive, "_rels/.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="{Relationships}/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            WritePart(archive, "xl/workbook.xml", $"""
                <workbook xmlns="{Sheet}" xmlns:r="{Relationships}">
                  <bookViews><workbookView/></bookViews>
                  <sheets><sheet name="Opening stock" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            WritePart(archive, "xl/_rels/workbook.xml.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="{Relationships}/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);

            using var stream = archive.CreateEntry(SheetPath).Open();
            using var writer = XmlWriter.Create(stream);
            writer.WriteStartElement("worksheet", Sheet.NamespaceName);
            writer.WriteRaw("""
                <sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>
                <cols>
                  <col min="1" max="1" width="22" customWidth="1"/>
                  <col min="2" max="2" width="45" customWidth="1"/>
                  <col min="3" max="5" width="25" customWidth="1"/>
                  <col min="6" max="6" width="29" customWidth="1"/>
                </cols>
                """);
            writer.WriteStartElement("sheetData", Sheet.NamespaceName);
            WriteRow(writer, 1, Headers);
            for (var i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = items[i];
                WriteRow(writer, i + 2, [item.Sku, item.Name, null, null, null, item.HasHistory ? "Already has stock history" : "Available"]);
            }
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        return output.ToArray();
    }

    internal static List<(int Line, string Sku, string[] Cells)> Read(Stream workbook, CancellationToken ct)
    {
        try
        {
            using var archive = new ZipArchive(workbook, ZipArchiveMode.Read, leaveOpen: true);
            var shared = archive.GetEntry("xl/sharedStrings.xml") is { } strings
                ? ReadPart(strings).Elements(Sheet + "si").Select(CellText).ToArray()
                : [];
            var sheet = archive.GetEntry(SheetPath) ?? throw Invalid("Use the downloaded opening stock workbook.");
            var sheetData = ReadPart(sheet).Element(Sheet + "sheetData") ?? throw Invalid("The opening stock worksheet is missing.");
            var rows = new List<(int Line, string Sku, string[] Cells)>();
            var headerFound = false;
            foreach (var row in sheetData.Elements(Sheet + "row"))
            {
                ct.ThrowIfCancellationRequested();
                var line = (int?)row.Attribute("r") ?? 0;
                var cells = Enumerable.Repeat("", Headers.Length).ToArray();
                foreach (var cell in row.Elements(Sheet + "c"))
                {
                    var reference = (string?)cell.Attribute("r");
                    if (reference is null || reference.Length < 2 || !char.IsAsciiDigit(reference[1])) continue;
                    var column = reference[0] - 'A';
                    if (column < 0 || column >= cells.Length) continue;
                    if (line > 1 && column is 1 or 5) continue;
                    if (cell.Element(Sheet + "f") is not null)
                        throw Invalid($"Row {line}: enter a value instead of a formula.");
                    var value = (string?)cell.Element(Sheet + "v") ?? "";
                    cells[column] = ((string?)cell.Attribute("t") switch
                    {
                        "s" => shared[int.Parse(value, CultureInfo.InvariantCulture)],
                        "inlineStr" => CellText(cell.Element(Sheet + "is")),
                        "b" or "e" or "d" => "Invalid value",
                        _ => value,
                    }).Trim();
                }
                if (line == 1)
                {
                    if (!cells.SequenceEqual(Headers)) throw Invalid("Keep the column headings from the downloaded workbook.");
                    headerFound = true;
                    continue;
                }
                if (cells[2].Length == 0 ||
                    (decimal.TryParse(cells[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var quantity) && quantity == 0))
                    continue;
                if (!headerFound || line < 2 || line > MaxSheetRows) throw Invalid("Use the downloaded opening stock workbook.");
                if (rows.Count >= StockDocumentService.MaxImportRows)
                    throw Invalid($"At most {StockDocumentService.MaxImportRows} filled rows per import.");
                rows.Add((line, Item.NormalizeSku(cells[0]), [cells[0], cells[2], cells[3], cells[4]]));
            }
            if (!headerFound) throw Invalid("Use the downloaded opening stock workbook.");
            return rows;
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            throw Invalid("The Excel file could not be read. Save a fresh copy of the opening stock workbook as .xlsx.");
        }
    }

    private static string CellText(XElement? node) =>
        node is null ? "" : string.Concat(node.Elements(Sheet + "t").Concat(node.Elements(Sheet + "r").Elements(Sheet + "t")).Select(x => x.Value));

    private static XElement ReadPart(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxXmlBytes) throw Invalid("The Excel worksheet is too large.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = MaxXmlBytes });
        return XElement.Load(reader);
    }

    private static void WritePart(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    private static void WriteRow(XmlWriter writer, int row, IReadOnlyList<string?> values)
    {
        var rowNumber = row.ToString(CultureInfo.InvariantCulture);
        writer.WriteStartElement("row", Sheet.NamespaceName);
        writer.WriteAttributeString("r", rowNumber);
        for (var column = 0; column < values.Count; column++)
        {
            if (values[column] is not { } value) continue;
            writer.WriteStartElement("c", Sheet.NamespaceName);
            writer.WriteAttributeString("r", (char)('A' + column) + rowNumber);
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is", Sheet.NamespaceName);
            writer.WriteElementString("t", Sheet.NamespaceName, value);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static ValidationException Invalid(string message) => new([new ValidationFailure("File", message)]);
}
